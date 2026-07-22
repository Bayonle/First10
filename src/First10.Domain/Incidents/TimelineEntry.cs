using First10.Domain.Channels;

namespace First10.Domain.Incidents;

public enum TimelineDirection
{
    Inbound = 0,
    Outbound = 1,
    /// <summary>State transitions and AI annotations — rendered distinct from messages (D-013).</summary>
    System = 2,
}

public enum TimelineEntryKind
{
    Text = 0,
    Image = 1,
    Voice = 2,
    LocationPin = 3,
    StatusChange = 4,
}

/// <summary>
/// One flat, two-way event stream per incident (D-012/D-013). The console renders this
/// verbatim; multi-reporter grouping is a render concern keyed on ConversationId.
/// </summary>
public class TimelineEntry
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public Guid ConversationId { get; set; }
    public TimelineDirection Direction { get; set; }
    public TimelineEntryKind Kind { get; set; }
    public string? Text { get; set; }
    public string? MediaRef { get; set; }

    /// <summary>Dedup key half: unique with <see cref="Channel"/> when set (all channels redeliver).</summary>
    public ChannelKind? Channel { get; set; }
    public string? ExternalMessageId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
