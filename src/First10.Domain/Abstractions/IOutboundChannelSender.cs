using First10.Domain.Channels;

namespace First10.Domain.Abstractions;

/// <summary>
/// Delivers an already-resolved outbound text to a reporter on one channel.
/// The Local channel has no sender — its timeline entry IS the delivery (the
/// cockpit renders it). Real channels (Telegram now, WhatsApp in M5) register one.
/// Throwing here fails the whole outbound handler transaction, so Wolverine's
/// durable retries make delivery at-least-once.
/// </summary>
public interface IOutboundChannelSender
{
    ChannelKind Channel { get; }

    Task SendAsync(string externalUserId, string text, CancellationToken ct);
}
