using First10.Domain.Triage;

namespace First10.Domain.Incidents;

public enum TicketStatus
{
    /// <summary>Created at session start (D-007); enriched as evidence arrives.</summary>
    Provisional = 0,
    /// <summary>Promotion rule met: (photo OR corroboration) AND location resolved.</summary>
    Promoted = 1,
    ExpiredUnverified = 2,
    Rejected = 3,
    Closed = 4,
    /// <summary>Folded into another incident by corroboration dedup — empty shell, hidden from queue.</summary>
    Merged = 5,
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

    /// <summary>Location is orthogonal to scene evidence: a pin (M2: or voice cue) sets this.
    /// Half of the M2 promotion rule — "(photo OR corroboration) AND location resolved".</summary>
    public DateTimeOffset? LocationResolvedAt { get; set; }

    /// <summary>One pin request per ticket (the paper's location-pin fallback flow).</summary>
    public DateTimeOffset? LocationRequestSentAt { get; set; }

    /// <summary>One "report complete, with dispatch" acknowledgment per ticket.</summary>
    public DateTimeOffset? AckSentAt { get; set; }

    /// <summary>Last pending-ask reminder / status reply — throttles the "never silent,
    /// never nagging" middle ground.</summary>
    public DateTimeOffset? LastReminderSentAt { get; set; }

    // ---- M2: extraction + micro-instructions + corroboration ----

    /// <summary>AI-extracted severity tier; errs high when uncertain (R3).</summary>
    public SeverityTier? Severity { get; set; }

    /// <summary>Free-text casualty estimate from extraction ("2-3 visible, 1 trapped").</summary>
    public string? CasualtyEstimate { get; set; }

    /// <summary>Which extractor produced the fields above ("heuristic-v1" / "chat-v1").</summary>
    public string? ExtractorVersion { get; set; }

    /// <summary>When the safety micro-instruction went out — the ≤30s latency metric
    /// (paper objective 4) is InstructionSentAt - CreatedAt.</summary>
    public DateTimeOffset? InstructionSentAt { get; set; }

    /// <summary>Distinct reporters attached to this incident. 2+ ⇒ corroborated (AUTO_VERIFY).</summary>
    public int ReporterCount { get; set; } = 1;

    /// <summary>Resolved incident location (from pin; M2+: voice cue). Drives 200m dedup.</summary>
    public double? LocationLat { get; set; }
    public double? LocationLng { get; set; }

    /// <summary>Where the coordinates came from — an inferred landmark is NEVER treated
    /// with pin-level trust (no corroboration merges; visibly approximate on the map;
    /// a real pin always replaces it).</summary>
    public LocationSource LocationSource { get; set; }

    /// <summary>Gazetteer key when the location was inferred from a named landmark.</summary>
    public string? LocationLandmark { get; set; }

    // ---- M3: dispatch loop-closure (paper §1.4; R1e: driven ONLY by explicit
    // dispatcher actions — these fields are set exclusively by DispatcherActionHandler) ----
    public DispatchState Dispatch { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset? ArrivedAt { get; set; }
    public DateTimeOffset? TransportedAt { get; set; }

    // ---- M3: timeline summarizer (paper §1.4 relay; R1f) ----
    public string? TimelineDigest { get; set; }
    /// <summary>⚠-joined contradiction list — surfaced, never hidden (R1f).</summary>
    public string? Contradictions { get; set; }
    public string? CrewBriefing { get; set; }
    public string? SummarizerVersion { get; set; }

    // ---- M3: dispatcher outcome marking — the labelled data for the weekly accuracy
    // review and the FP-rate metric (paper objective 3) ----
    public TicketOutcome? Outcome { get; set; }
    public DateTimeOffset? OutcomeAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum SeverityTier
{
    Low = 0,
    Medium = 1,
    High = 2,
}

public enum DispatchState
{
    None = 0,
    Dispatched = 1,
    Arrived = 2,
    Transported = 3,
}

public enum TicketOutcome
{
    Real = 0,
    False = 1,
    Unverifiable = 2,
}

public enum LocationSource
{
    None = 0,
    Pin = 1,
    /// <summary>Approximate: extraction matched a corridor landmark in the report text.</summary>
    LandmarkInferred = 2,
}
