using First10.Application.Ingest;
using First10.Application.Outbound;
using First10.Application.Sessions;
using First10.Application.Summaries;
using First10.Domain.Abstractions;
using First10.Domain.Channels;
using First10.Domain.Conversations;
using First10.Domain.Incidents;
using First10.Domain.Triage;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace First10.Application.Dispatch;

public enum DispatcherActionKind
{
    Dispatched = 1,
    Arrived = 2,
    Transported = 3,
}

/// <summary>An explicit human dispatcher decision — the ONLY source of loop-closure
/// messages (R1e). Runs through the outbox: no committed action, no message.</summary>
public sealed record DispatcherAction(Guid TicketId, DispatcherActionKind Kind, string Officer);

public sealed record MarkOutcome(Guid TicketId, TicketOutcome Outcome, string Officer, string? Note);

public static class DispatcherActionHandler
{
    public static async Task<OutgoingMessages> Handle(
        DispatcherAction action,
        First10DbContext db,
        ITimelineSummarizer summarizer,
        ILogger logger,
        CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == action.TicketId, ct);
        if (ticket is null || ticket.Status is TicketStatus.Merged or TicketStatus.Rejected or TicketStatus.Closed)
        {
            logger.LogWarning("Dispatcher action {Kind} on unavailable ticket {TicketId} ignored", action.Kind, action.TicketId);
            return outgoing;
        }

        // Transitions are strictly ordered; repeats and skips are ignored, not errored —
        // a double-clicked button must not double-message reporters.
        var valid = (action.Kind, ticket.Dispatch) switch
        {
            (DispatcherActionKind.Dispatched, DispatchState.None) => true,
            (DispatcherActionKind.Arrived, DispatchState.Dispatched) => true,
            (DispatcherActionKind.Transported, DispatchState.Arrived) => true,
            _ => false,
        };
        if (!valid)
        {
            logger.LogInformation("Dispatcher action {Kind} invalid from state {State}; ignored", action.Kind, ticket.Dispatch);
            return outgoing;
        }

        var now = DateTimeOffset.UtcNow;
        var (newState, noticeKind) = action.Kind switch
        {
            DispatcherActionKind.Dispatched => (DispatchState.Dispatched, OutboundKind.DispatchedNotice),
            DispatcherActionKind.Arrived => (DispatchState.Arrived, OutboundKind.ArrivedNotice),
            _ => (DispatchState.Transported, OutboundKind.TransportedNotice),
        };

        // Queryable audit row, same transaction as the state change: the audit can
        // never disagree with the ticket (complements the human-readable timeline note).
        db.AccessLogs.Add(new AccessLogRecord
        {
            Id = Guid.NewGuid(),
            Kind = AccessKind.DispatcherAction,
            Who = action.Officer,
            TicketId = ticket.Id,
            Detail = newState.ToString().ToLowerInvariant(),
            At = now,
        });

        ticket.Dispatch = newState;
        ticket.UpdatedAt = now;
        switch (action.Kind)
        {
            case DispatcherActionKind.Dispatched:
                ticket.DispatchedAt = now;
                // Time-to-dispatch: THE pilot headline metric (paper objective 1).
                var timeToDispatch = (now - ticket.CreatedAt).TotalMinutes;
                AddNote(db, ticket, $"DISPATCHED by {action.Officer} · time-to-dispatch {timeToDispatch:F1} min");
                // Crew briefing is generated at dispatch (paper §1.4) — best effort.
                try
                {
                    var briefing = await summarizer.BriefCrewAsync(await TimelineSnapshot.Build(db, ticket, ct), ct);
                    ticket.CrewBriefing = briefing.Length <= 4096 ? briefing : briefing[..4096];
                    AddNote(db, ticket, "Crew briefing generated");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Crew briefing failed for {TicketId}; dispatch proceeds without it", ticket.Id);
                }
                break;
            case DispatcherActionKind.Arrived:
                ticket.ArrivedAt = now;
                AddNote(db, ticket, $"ARRIVED marked by {action.Officer}");
                break;
            case DispatcherActionKind.Transported:
                ticket.TransportedAt = now;
                ticket.Status = TicketStatus.Closed;
                AddNote(db, ticket, $"TRANSPORTED marked by {action.Officer} — incident closed");
                // Free every contributing conversation for future incidents.
                foreach (var conversation in await ContributingConversations(db, ticket.Id, ct))
                {
                    if (conversation.ActiveTicketId == ticket.Id) conversation.ActiveTicketId = null;
                }
                outgoing.Add(new SessionEnded(ticket.Id));
                break;
        }

        // Loop closure to EVERY reporter who contributed (paper §1.4) — status only,
        // never victim identity or medical detail (§1.5).
        foreach (var conversation in await ContributingConversations(db, ticket.Id, ct))
        {
            outgoing.Add(new SendOutboundMessage(conversation.Id, ticket.Id, noticeKind, ticket.Language ?? "english"));
        }

        outgoing.Add(new TicketUpserted(ticket.Id));
        return outgoing;
    }

    public static async Task<OutgoingMessages> Handle(
        MarkOutcome command,
        First10DbContext db,
        TriageOptions options,
        ILogger logger,
        CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == command.TicketId, ct);
        if (ticket is null || ticket.Status == TicketStatus.Merged)
        {
            return outgoing;
        }

        var now = DateTimeOffset.UtcNow;
        db.AccessLogs.Add(new AccessLogRecord
        {
            Id = Guid.NewGuid(),
            Kind = AccessKind.DispatcherAction,
            Who = command.Officer,
            TicketId = ticket.Id,
            Detail = $"outcome:{command.Outcome}",
            At = now,
        });
        ticket.Outcome = command.Outcome;
        ticket.OutcomeAt = now;
        ticket.UpdatedAt = now;
        AddNote(db, ticket, $"Outcome marked {command.Outcome} by {command.Officer}"
            + (command.Note is { Length: > 0 } ? $": {command.Note}" : ""));

        if (command.Outcome == TicketOutcome.False)
        {
            ticket.Status = TicketStatus.Rejected;
            // Sticky reputation hit (D-008): every contributing reporter of a
            // dispatcher-confirmed false report drops to Low trust.
            foreach (var conversation in await ContributingConversations(db, ticket.Id, ct))
            {
                var reputation = await db.ReporterReputations.SingleOrDefaultAsync(
                    r => r.Channel == conversation.Channel && r.ExternalUserId == conversation.ExternalUserId, ct);
                if (reputation is null)
                {
                    db.ReporterReputations.Add(new ReporterReputation
                    {
                        Id = Guid.NewGuid(),
                        Channel = conversation.Channel,
                        ExternalUserId = conversation.ExternalUserId,
                        Trust = TrustLevel.Low,
                        Note = $"Dispatcher-confirmed false report {now:yyyy-MM-dd} (ticket {ticket.Id:N})",
                        UpdatedAt = now,
                    });
                }
                else if (reputation.Trust > TrustLevel.Low)
                {
                    reputation.Trust = TrustLevel.Low;
                    reputation.Note = $"Dispatcher-confirmed false report {now:yyyy-MM-dd} (ticket {ticket.Id:N})";
                    reputation.UpdatedAt = now;
                }
                if (conversation.ActiveTicketId == ticket.Id) conversation.ActiveTicketId = null;
            }
            outgoing.Add(new SessionEnded(ticket.Id));
        }

        outgoing.Add(new TicketUpserted(ticket.Id));
        return outgoing;
    }

    private static Task<List<Conversation>> ContributingConversations(
        First10DbContext db, Guid ticketId, CancellationToken ct) =>
        db.TimelineEntries
            .Where(e => e.TicketId == ticketId && e.Direction == TimelineDirection.Inbound)
            .Select(e => e.ConversationId)
            .Distinct()
            .Join(db.Conversations, id => id, c => c.Id, (_, c) => c)
            .ToListAsync(ct);

    private static void AddNote(First10DbContext db, IncidentTicket ticket, string text)
    {
        var conversationId = db.TimelineEntries.Local
            .Concat(db.TimelineEntries.Where(e => e.TicketId == ticket.Id))
            .Select(e => e.ConversationId)
            .FirstOrDefault();
        db.TimelineEntries.Add(new TimelineEntry
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            ConversationId = conversationId,
            Direction = TimelineDirection.System,
            Kind = TimelineEntryKind.StatusChange,
            Text = text,
            OccurredAt = DateTimeOffset.UtcNow,
        });
    }
}
