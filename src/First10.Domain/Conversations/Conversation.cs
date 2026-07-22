using First10.Domain.Channels;

namespace First10.Domain.Conversations;

/// <summary>
/// One persistent thread per reporter per channel. Identity is (Channel, ExternalUserId)
/// — never a bare phone number (D-005).
/// </summary>
public class Conversation
{
    public Guid Id { get; set; }
    public ChannelKind Channel { get; set; }
    public string ExternalUserId { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastInboundAt { get; set; }

    /// <summary>The currently open ticket this conversation feeds, if any (M0 stub session).</summary>
    public Guid? ActiveTicketId { get; set; }
}
