using First10.Application.Ingest;
using First10.Domain.Abstractions;
using First10.Domain.Channels;
using First10.Domain.Incidents;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace First10.Application.Outbound;

/// <summary>
/// The single outbound handler for every channel (D-005). Resolves the text (clinical
/// content strictly from the approved store, never composed — D-011), records the
/// Outbound timeline entry (the console's evidence of what we told the reporter),
/// and routes delivery to the conversation channel's registered sender. Local needs
/// no sender — the timeline entry is what the cockpit renders. A real channel with
/// no registered sender logs an error and still keeps the timeline record: what we
/// FAILED to deliver must stay visible, not vanish.
/// </summary>
public static class OutboundMessageHandler
{
    public static async Task<TicketUpserted?> Handle(
        SendOutboundMessage command,
        First10DbContext db,
        IEnumerable<IOutboundChannelSender> senders,
        ILogger logger,
        CancellationToken ct)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == command.ConversationId, ct);
        if (conversation is null)
        {
            logger.LogWarning("Outbound for unknown conversation {ConversationId} dropped", command.ConversationId);
            return null;
        }

        string text;
        string? audioRef = null;
        if (command.Kind == OutboundKind.MicroInstruction)
        {
            // Clinical content is resolved from the approved store, never composed (D-011).
            var template = await db.MicroInstructionTemplates
                .SingleOrDefaultAsync(t => t.Id == command.TemplateId, ct);
            if (template is null)
            {
                logger.LogError("MicroInstruction outbound without resolvable template {TemplateId}", command.TemplateId);
                return null;
            }
            text = template.Text;
            audioRef = template.AudioMediaRef;
        }
        else
        {
            text = OutboundTexts.For(command.Kind, command.Language);
        }

        if (conversation.Channel != ChannelKind.Local)
        {
            var sender = senders.FirstOrDefault(s => s.Channel == conversation.Channel);
            if (sender is null)
            {
                logger.LogError("No outbound sender registered for channel {Channel}; message recorded but NOT delivered",
                    conversation.Channel);
            }
            else
            {
                // Throws on channel failure → whole handler retries durably (at-least-once).
                await sender.SendAsync(conversation.ExternalUserId, text, ct);
            }
        }

        db.TimelineEntries.Add(new TimelineEntry
        {
            Id = Guid.NewGuid(),
            TicketId = command.TicketId,
            ConversationId = conversation.Id,
            Direction = TimelineDirection.Outbound,
            Kind = audioRef is not null ? TimelineEntryKind.Voice : TimelineEntryKind.Text,
            Text = text,
            MediaRef = audioRef,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        return command.TicketId is { } ticketId ? new TicketUpserted(ticketId) : null;
    }
}
