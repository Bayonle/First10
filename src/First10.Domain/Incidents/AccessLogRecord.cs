namespace First10.Domain.Incidents;

public enum AccessKind
{
    /// <summary>A short-lived signed media URL was issued to a console user.</summary>
    MediaUrlIssued = 0,

    /// <summary>A ticket timeline (evidence view) was opened.</summary>
    TicketViewed = 1,

    /// <summary>The retention sweep deleted a media blob past its window (Who = "retention-job").</summary>
    MediaDeleted = 2,
}

/// <summary>
/// Audit trail for evidence access (§7.1: every media access logged with who /
/// incident / mediaRef / when). Rows are append-only and outlive the media they
/// reference — the retention job deletes media, never audit rows.
/// </summary>
public class AccessLogRecord
{
    public Guid Id { get; set; }
    public AccessKind Kind { get; set; }
    public string Who { get; set; } = default!;
    public string? MediaRef { get; set; }
    public Guid? TicketId { get; set; }
    public DateTimeOffset At { get; set; }
}
