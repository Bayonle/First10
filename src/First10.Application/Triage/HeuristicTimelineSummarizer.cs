using First10.Domain.Abstractions;

namespace First10.Application.Triage;

/// <summary>
/// Dev/CI fallback: mechanical digest, no contradiction detection (that genuinely
/// needs language understanding — the chat summarizer is the real one, D-003).
/// </summary>
public sealed class HeuristicTimelineSummarizer : ITimelineSummarizer
{
    public const string Version = "heuristic-summarize-v1";

    public Task<TimelineSummary> SummarizeAsync(TimelineSummaryInput input, CancellationToken ct)
    {
        var first = input.Entries.Count > 0 ? input.Entries[0].At.ToString("HH:mm") : "?";
        var digest = $"{input.ReporterCount} reporter(s), first report {first}. "
            + $"{input.Entries.Count} timeline events. Location: {input.ResolvedLocation ?? "unresolved"}.";
        return Task.FromResult(new TimelineSummary(digest, [], Version));
    }

    public Task<string> BriefCrewAsync(TimelineSummaryInput input, CancellationToken ct)
    {
        var texts = input.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Content))
            .Select(e => $"- [{e.At:HH:mm}] {e.Reporter}: {e.Content}");
        return Task.FromResult(
            $"CREW BRIEFING (mechanical fallback — no AI summary available)\n"
            + $"Reporters: {input.ReporterCount}. Location: {input.ResolvedLocation ?? "unresolved"}.\n"
            + string.Join('\n', texts.Take(12)));
    }
}
