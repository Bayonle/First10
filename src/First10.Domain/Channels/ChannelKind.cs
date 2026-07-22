namespace First10.Domain.Channels;

public enum ChannelKind
{
    /// <summary>Dev-only chat cockpit. Must never be reachable in production (D-006).</summary>
    Local = 0,
    WhatsApp = 1,
    Telegram = 2,
}
