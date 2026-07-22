namespace First10.Domain.Incidents;

public enum TicketStatus
{
    /// <summary>Created at session start (D-007); enriched as evidence arrives.</summary>
    Provisional = 0,
    Promoted = 1,
    ExpiredUnverified = 2,
    Rejected = 3,
    Closed = 4,
}

/// <summary>
/// The incident aggregate the dispatcher sees. Created the moment a session opens —
/// never at the end (D-007). Shared across reporter sessions once dedup lands (M2).
/// </summary>
public class IncidentTicket
{
    public Guid Id { get; set; }
    public TicketStatus Status { get; set; }

    /// <summary>One-line dispatcher summary. Raw first-message text in M0; AI-written from M2.</summary>
    public string Summary { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
