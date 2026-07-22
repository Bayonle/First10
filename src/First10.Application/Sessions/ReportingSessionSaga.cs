using First10.Application.Ingest;
using First10.Application.Outbound;
using First10.Domain.Incidents;
using First10.Domain.Triage;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace First10.Application.Sessions;

/// <summary>
/// Per-ticket session lifecycle saga (M2, replaces the lazy checks as the primary
/// mechanism — the lazy ingest checks remain as a backstop). Owns the durable timers:
/// pin reminder (R5c, 30s), challenge expiry, session-age cap. The DATABASE ticket is
/// the authoritative state; timer handlers re-check it and no-op when the condition
/// resolved before the timer fired — so nothing needs cancelling.
/// </summary>
public class ReportingSessionSaga : Saga
{
    public Guid Id { get; set; }
    public DateTimeOffset OpenedAt { get; set; }

    public static (ReportingSessionSaga, OutgoingMessages) Start(SessionOpened opened, TriageOptions options)
    {
        var saga = new ReportingSessionSaga { Id = opened.TicketId, OpenedAt = DateTimeOffset.UtcNow };
        var outgoing = new OutgoingMessages();

        if (opened.PinAskPending)
        {
            outgoing.Add(new PinReminderDue(opened.TicketId)
                .DelayedFor(TimeSpan.FromSeconds(options.PinReminderSeconds)));
        }
        if (opened.ChallengePending)
        {
            outgoing.Add(new ChallengeExpiryDue(opened.TicketId)
                .DelayedFor(TimeSpan.FromMinutes(options.ChallengeExpiryMinutes)));
        }
        outgoing.Add(new SessionAgeCapDue(opened.TicketId)
            .DelayedFor(TimeSpan.FromMinutes(options.SessionMaxAgeMinutes)));

        return (saga, outgoing);
    }

    /// <summary>R5c: one proactive reminder if the pin is still missing.</summary>
    public async Task<OutgoingMessages> Handle(
        PinReminderDue due, First10DbContext db, ILogger logger, CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == due.TicketId, ct);
        if (ticket is null || ticket.Status != TicketStatus.Provisional || ticket.LocationResolvedAt is not null)
        {
            return outgoing; // resolved before the timer — no-op
        }

        var conversationId = await ConversationOf(db, ticket.Id, ct);
        if (conversationId is null) return outgoing;

        ticket.LastReminderSentAt = DateTimeOffset.UtcNow;
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        AddSystemNote(db, ticket.Id, conversationId.Value, "Pin reminder sent (30s, no location yet)");

        var kind = ticket.Evidence >= EvidenceLevel.VoiceOnly
            ? OutboundKind.LocationPinRequest      // has scene evidence, needs pin
            : OutboundKind.ElicitationChallenge;   // still needs everything
        outgoing.Add(new SendOutboundMessage(conversationId.Value, ticket.Id, kind, ticket.Language ?? "english"));
        outgoing.Add(new TicketUpserted(ticket.Id));
        return outgoing;
    }

    /// <summary>Unanswered challenge → ExpiredUnverified (visible; dispatcher makes the kill call).</summary>
    public async Task<OutgoingMessages> Handle(
        ChallengeExpiryDue due, First10DbContext db, ILogger logger, CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == due.TicketId, ct);
        if (ticket is null || ticket.Status != TicketStatus.Provisional
            || ticket.Evidence > EvidenceLevel.TextOnly || ticket.LocationResolvedAt is not null)
        {
            return outgoing; // answered (or terminal) — the age cap remains armed
        }

        await Expire(db, ticket, "challenge expired with no evidence or location", outgoing, ct);
        MarkCompleted();
        return outgoing;
    }

    /// <summary>Proactive age cap — the treadmill fix, now without needing a next message.</summary>
    public async Task<OutgoingMessages> Handle(
        SessionAgeCapDue due, First10DbContext db, ILogger logger, CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == due.TicketId, ct);
        MarkCompleted(); // the age cap is always the saga's end, whatever we find

        if (ticket is null || ticket.Status != TicketStatus.Provisional && ticket.Status != TicketStatus.Promoted)
        {
            return outgoing;
        }

        var challengeUnanswered = ticket.Status == TicketStatus.Provisional
            && ticket.ChallengeSentAt is not null
            && ticket.Evidence <= EvidenceLevel.TextOnly
            && ticket.LocationResolvedAt is null;

        if (challengeUnanswered)
        {
            await Expire(db, ticket, "session age cap reached, challenge never answered", outgoing, ct);
        }
        else
        {
            // Evidence/location exist — incident stays pending for dispatch; only the
            // reporter session closes.
            var conversationId = await ConversationOf(db, ticket.Id, ct);
            if (conversationId is not null)
            {
                AddSystemNote(db, ticket.Id, conversationId.Value,
                    "Reporter session closed (age cap) — later messages open a new incident");
            }
            await DetachConversations(db, ticket.Id, ct);
            ticket.UpdatedAt = DateTimeOffset.UtcNow;
            outgoing.Add(new TicketUpserted(ticket.Id));
        }
        return outgoing;
    }

    public void Handle(SessionEnded _) => MarkCompleted();

    private static async Task Expire(
        First10DbContext db, IncidentTicket ticket, string reason, OutgoingMessages outgoing, CancellationToken ct)
    {
        ticket.Status = TicketStatus.ExpiredUnverified;
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        var conversationId = await ConversationOf(db, ticket.Id, ct);
        if (conversationId is not null)
        {
            AddSystemNote(db, ticket.Id, conversationId.Value, $"Session expired: {reason}");
        }
        await DetachConversations(db, ticket.Id, ct);
        outgoing.Add(new TicketUpserted(ticket.Id));
    }

    private static async Task DetachConversations(First10DbContext db, Guid ticketId, CancellationToken ct)
    {
        var conversations = await db.Conversations.Where(c => c.ActiveTicketId == ticketId).ToListAsync(ct);
        foreach (var conversation in conversations)
        {
            conversation.ActiveTicketId = null;
        }
    }

    private static Task<Guid?> ConversationOf(First10DbContext db, Guid ticketId, CancellationToken ct) =>
        db.TimelineEntries.Where(e => e.TicketId == ticketId)
            .OrderBy(e => e.OccurredAt)
            .Select(e => (Guid?)e.ConversationId)
            .FirstOrDefaultAsync(ct);

    private static void AddSystemNote(First10DbContext db, Guid ticketId, Guid conversationId, string text) =>
        db.TimelineEntries.Add(new TimelineEntry
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            ConversationId = conversationId,
            Direction = TimelineDirection.System,
            Kind = TimelineEntryKind.StatusChange,
            Text = text,
            OccurredAt = DateTimeOffset.UtcNow,
        });
}
