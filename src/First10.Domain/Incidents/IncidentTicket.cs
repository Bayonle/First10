using First10.Domain.Triage;

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

    /// <summary>One-line dispatcher summary. Raw first-message text until AI extraction (M2).</summary>
    public string Summary { get; set; } = default!;

    // ---- Triage (M1, D-008) ----
    public Disposition Disposition { get; set; }
    public EvidenceLevel Evidence { get; set; }
    /// <summary>"english" | "pidgin" | "yoruba" — from Stage 1.</summary>
    public string? Language { get; set; }
    /// <summary>Comma-joined triage flags ("reused-image", "outside-corridor", …).</summary>
    public string? Flags { get; set; }
    public string? ClassifierVersion { get; set; }
    /// <summary>Set when the elicitation challenge went out — one challenge per ticket.</summary>
    public DateTimeOffset? ChallengeSentAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
