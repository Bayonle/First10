using First10.Domain.Abstractions;
using Microsoft.Extensions.AI;

namespace First10.Infrastructure.Ai;

/// <summary>
/// Timeline summarizer behind IChatClient (R1f). Its contract is the inverse of the
/// extractor's: where witnesses disagree it must REPORT the disagreement, never
/// average it into one confident line.
/// </summary>
public sealed class ChatTimelineSummarizer(IChatClient chatClient) : ITimelineSummarizer
{
    public const string Version = "chat-summarize-v1";

    private const string SummarySystemPrompt =
        """
        You digest a live multi-reporter crash-report timeline for an FRSC dispatcher on
        the Berger-Mowe stretch of the Lagos-Ibadan Expressway. Reporters are bystanders
        messaging on WhatsApp in English, Nigerian Pidgin, or Yoruba.

        Produce:
        - digest: 2-3 short lines, English. Line 1: consensus picture (what, where, how
          many reporters, first report time). Line 2+: per-source attribution for facts
          only ONE reporter claims ("fire: reporter 1 only"). Facts you cannot attribute
          do not exist — never invent.
        - contradictions: an array of strings, one per GENUINE disagreement, quoting both
          sides with reporter numbers and times, e.g.:
          "Reporter 1 (14:02): 'two people trapped' vs Reporter 3 (14:09): 'everybody don comot'".
          Include cross-modal conflicts (narrative names one landmark, the resolved pin
          is elsewhere; photo content vs claims; retraction vs continued reports).
          CRITICAL: do not smooth over or resolve disagreements — surfacing them IS the
          job. Empty array when there are none. Restatements, elaborations, or partial
          info are NOT contradictions.
        """;

    private const string BriefingSystemPrompt =
        """
        Write an on-arrival briefing for the FRSC/ambulance crew responding to a crash on
        the Lagos-Ibadan Expressway, from the reporter timeline provided. 4-6 short lines,
        English, plain language, most-operational-first:
        - What/where (with landmark), when first reported, how many independent reporters
        - Casualties/hazards as reported, with per-reporter attribution where sources differ
        - The most recent update from the timeline, with its timestamp — OMIT this line
          entirely if nothing new was reported after the initial accounts
        - Open contradictions the crew should verify on arrival
        Never include reporter phone numbers or victim identity.
        CRITICAL: every fact and every timestamp MUST come from the timeline provided.
        A briefing with a missing detail is safe; a briefing with an invented detail
        sends a crew in with false expectations.
        """;

    public async Task<TimelineSummary> SummarizeAsync(TimelineSummaryInput input, CancellationToken ct)
    {
        var response = await chatClient.GetResponseAsync<SummaryWire>(
            [
                new ChatMessage(ChatRole.System, SummarySystemPrompt),
                new ChatMessage(ChatRole.User, Render(input)),
            ],
            cancellationToken: ct);

        return new TimelineSummary(
            string.IsNullOrWhiteSpace(response.Result.Digest) ? "(no digest)" : response.Result.Digest,
            response.Result.Contradictions ?? [],
            Version);
    }

    public async Task<string> BriefCrewAsync(TimelineSummaryInput input, CancellationToken ct)
    {
        var response = await chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, BriefingSystemPrompt),
                new ChatMessage(ChatRole.User, Render(input)),
            ],
            cancellationToken: ct);
        return response.Text;
    }

    private static string Render(TimelineSummaryInput input)
    {
        var lines = input.Entries.Select(e => $"[{e.At:HH:mm:ss}] {e.Reporter} ({e.Kind}): {e.Content}");
        return $"Reporters: {input.ReporterCount}. Resolved location: {input.ResolvedLocation ?? "none"}.\n"
            + string.Join('\n', lines);
    }

    private sealed record SummaryWire(string? Digest, List<string>? Contradictions);
}
