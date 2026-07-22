using First10.Application.Ingest;
using First10.Domain.Channels;
using First10.Domain.Incidents;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace First10.Application.Outbound;

/// <summary>
/// The Local channel's outbound sender (D-005/D-006): "sending" means appending an
/// Outbound timeline entry, which the cockpit renders as the system's reply. Real
/// channel senders (WhatsApp M5, Telegram later) get their own handlers keyed on
/// the conversation's channel.
/// </summary>
public static class LocalOutboundHandler
{
    public static async Task<TicketUpserted?> Handle(
        SendOutboundMessage command,
        First10DbContext db,
        ILogger logger,
        CancellationToken ct)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == command.ConversationId, ct);
        if (conversation is null)
        {
            logger.LogWarning("Outbound for unknown conversation {ConversationId} dropped", command.ConversationId);
            return null;
        }

        if (conversation.Channel != ChannelKind.Local)
        {
            // Per-channel dispatch becomes a routing concern when real channels land (M5).
            logger.LogWarning("No sender for channel {Channel} yet; outbound dropped", conversation.Channel);
            return null;
        }

        db.TimelineEntries.Add(new TimelineEntry
        {
            Id = Guid.NewGuid(),
            TicketId = command.TicketId,
            ConversationId = conversation.Id,
            Direction = TimelineDirection.Outbound,
            Kind = TimelineEntryKind.Text,
            Text = OutboundTexts.For(command.Kind, command.Language),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        return command.TicketId is { } ticketId ? new TicketUpserted(ticketId) : null;
    }
}
