using First10.Application.Ingest;
using First10.Domain.Abstractions;
using First10.Domain.Incidents;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace First10.Application.Summaries;

/// <summary>Cascaded after extraction when the timeline warrants a digest.</summary>
public sealed record RunSummarization(Guid TicketId, Guid ConversationId);

public static class RunSummarizationHandler
{
    public static async Task<OutgoingMessages> Handle(
        RunSummarization command,
        First10DbContext db,
        ITimelineSummarizer summarizer,
        ILogger logger,
        CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == command.TicketId, ct);
        if (ticket is null || ticket.Status is TicketStatus.Merged or TicketStatus.Rejected)
        {
            return outgoing;
        }

        var input = await TimelineSnapshot.Build(db, ticket, ct);

        TimelineSummary summary;
        try
        {
            summary = await summarizer.SummarizeAsync(input, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Summarization failed for {TicketId}; keeping previous digest", ticket.Id);
            return outgoing;
        }

        var newContradictions = summary.Contradictions.Count > 0
            ? string.Join(" ⚠ ", summary.Contradictions)
            : null;

        // Contradictions are load-bearing (R1f): announce changes as a system note so
        // the dispatcher sees NEW disagreements appear in the timeline, not just a
        // silently mutating panel.
        if (newContradictions != ticket.Contradictions && newContradictions is not null)
        {
            db.TimelineEntries.Add(new TimelineEntry
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                ConversationId = command.ConversationId,
                Direction = TimelineDirection.System,
                Kind = TimelineEntryKind.StatusChange,
                Text = $"⚠ CONTRADICTION: {string.Join(" ⚠ ", summary.Contradictions)}",
                OccurredAt = DateTimeOffset.UtcNow,
            });
        }

        ticket.TimelineDigest = Truncate(summary.Digest, 2048);
        ticket.Contradictions = Truncate(newContradictions, 4096);
        ticket.SummarizerVersion = summary.Version;
        ticket.UpdatedAt = DateTimeOffset.UtcNow;

        outgoing.Add(new TicketUpserted(ticket.Id));
        return outgoing;
    }

    private static string? Truncate(string? value, int max) =>
        value is { Length: > 0 } ? (value.Length <= max ? value : value[..max]) : null;
}

/// <summary>Builds the reporter-anonymous snapshot both the digest and briefing use.</summary>
public static class TimelineSnapshot
{
    public static async Task<TimelineSummaryInput> Build(First10DbContext db, IncidentTicket ticket, CancellationToken ct)
    {
        var entries = await db.TimelineEntries
            .Where(e => e.TicketId == ticket.Id)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(ct);

        // Number reporters by first appearance — identity stays out of AI inputs.
        var reporterNumbers = new Dictionary<Guid, int>();
        foreach (var conversationId in entries.Where(e => e.Direction == TimelineDirection.Inbound)
                     .Select(e => e.ConversationId))
        {
            if (!reporterNumbers.ContainsKey(conversationId))
            {
                reporterNumbers[conversationId] = reporterNumbers.Count + 1;
            }
        }

        var snapshot = entries
            .Where(e => e.Direction != TimelineDirection.Outbound) // system asks aren't witness data
            .Select(e => new TimelineSnapshotEntry(
                e.Direction == TimelineDirection.System
                    ? "system"
                    : $"Reporter {reporterNumbers.GetValueOrDefault(e.ConversationId, 0)}",
                e.Kind switch
                {
                    TimelineEntryKind.Voice => "voice(transcript)",
                    TimelineEntryKind.Image => "photo",
                    TimelineEntryKind.LocationPin => "pin",
                    TimelineEntryKind.StatusChange => "note",
                    _ => "text",
                },
                e.Kind == TimelineEntryKind.Voice ? (e.TranscriptText ?? "[no transcript]") : e.Text,
                e.OccurredAt))
            .ToList();

        var location = ticket.LocationLat is { } lat && ticket.LocationLng is { } lng
            ? $"{lat:F5}, {lng:F5}"
            : null;

        return new TimelineSummaryInput(snapshot, Math.Max(1, reporterNumbers.Count), location);
    }
}
