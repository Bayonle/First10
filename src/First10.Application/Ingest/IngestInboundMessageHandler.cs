using First10.Domain.Channels;
using First10.Domain.Conversations;
using First10.Domain.Incidents;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace First10.Application.Ingest;

/// <summary>
/// The channel-agnostic entry point of the core pipeline. Adapters publish a normalized
/// <see cref="InboundChannelMessage"/>; this handler dedupes, resolves the conversation,
/// opens/extends the (M0 stub) session, and appends to the incident timeline.
///
/// M1 replaces the stub session-start with the triage funnel (Stage 0/1, dispositions);
/// M2 replaces it with the ReportingSession saga. The dedup + conversation + timeline
/// mechanics here are permanent.
/// </summary>
public static class IngestInboundMessageHandler
{
    public static async Task<TicketUpserted?> Handle(
        InboundChannelMessage message,
        First10DbContext db,
        ILogger logger,
        CancellationToken ct)
    {
        // Dedup (D-005): every channel redelivers. Fast path here; the unique index
        // on (Channel, ExternalMessageId) is the authoritative backstop under races.
        var isDuplicate = await db.TimelineEntries.AnyAsync(
            t => t.Channel == message.Channel && t.ExternalMessageId == message.ExternalMessageId, ct);
        if (isDuplicate)
        {
            logger.LogInformation(
                "Duplicate delivery dropped: {Channel}/{ExternalMessageId}",
                message.Channel, message.ExternalMessageId);
            return null;
        }

        var now = DateTimeOffset.UtcNow;

        // Conversation keyed by (Channel, ExternalUserId) — D-005.
        var conversation = await db.Conversations.SingleOrDefaultAsync(
            c => c.Channel == message.Channel && c.ExternalUserId == message.ExternalUserId, ct);
        if (conversation is null)
        {
            conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                Channel = message.Channel,
                ExternalUserId = message.ExternalUserId,
                CreatedAt = now,
            };
            db.Conversations.Add(conversation);
        }
        conversation.LastInboundAt = now;

        // M0 stub session: first message opens a provisional ticket immediately (D-007);
        // subsequent messages from the same conversation enrich it.
        IncidentTicket? ticket = conversation.ActiveTicketId is { } activeId
            ? await db.Tickets.SingleOrDefaultAsync(t => t.Id == activeId && t.Status == TicketStatus.Provisional, ct)
            : null;

        if (ticket is null)
        {
            ticket = new IncidentTicket
            {
                Id = Guid.NewGuid(),
                Status = TicketStatus.Provisional,
                Summary = Summarize(message),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Tickets.Add(ticket);
            conversation.ActiveTicketId = ticket.Id;
        }
        else
        {
            ticket.UpdatedAt = now;
        }

        db.TimelineEntries.Add(new TimelineEntry
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            ConversationId = conversation.Id,
            Direction = TimelineDirection.Inbound,
            Kind = ToTimelineKind(message.Kind),
            Text = message.Text,
            MediaRef = message.MediaRef,
            Channel = message.Channel,
            ExternalMessageId = message.ExternalMessageId,
            OccurredAt = message.OccurredAt,
        });

        // Cascaded through the durable outbox: only published if this transaction commits.
        return new TicketUpserted(ticket.Id);
    }

    private static string Summarize(InboundChannelMessage message) =>
        message.Kind switch
        {
            InboundKind.Text when !string.IsNullOrWhiteSpace(message.Text) =>
                message.Text.Length <= 140 ? message.Text : message.Text[..140],
            InboundKind.Image => "[photo received]",
            InboundKind.Voice => "[voice note received]",
            InboundKind.LocationPin => "[location pin received]",
            _ => "[message received]",
        };

    private static TimelineEntryKind ToTimelineKind(InboundKind kind) =>
        kind switch
        {
            InboundKind.Text => TimelineEntryKind.Text,
            InboundKind.Image => TimelineEntryKind.Image,
            InboundKind.Voice => TimelineEntryKind.Voice,
            InboundKind.LocationPin => TimelineEntryKind.LocationPin,
            _ => TimelineEntryKind.Text,
        };
}
