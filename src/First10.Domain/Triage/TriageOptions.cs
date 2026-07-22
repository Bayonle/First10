using First10.Domain.Channels;

namespace First10.Domain.Triage;

/// <summary>
/// Stage 0 thresholds. Config-bound from the "Triage" section; defaults are pilot
/// starting points, tuned during the weekly accuracy review.
/// </summary>
public class TriageOptions
{
    /// <summary>Max new-incident (ticket-opening) attempts per conversation per window.</summary>
    public int MaxNewIncidentsPerWindow { get; set; } = 30;

    public int RateLimitWindowMinutes { get; set; } = 60;

    /// <summary>Distinct conversations opening incidents within the flood window to trip R11.</summary>
    public int FloodDistinctConversations { get; set; } = 8;

    public int FloodWindowMinutes { get; set; } = 10;

    /// <summary>Max Hamming distance between 64-bit dHashes to count as a reused image.</summary>
    public int PerceptualHashThreshold { get; set; } = 10;

    /// <summary>
    /// Reporter-session boundary (lazy, evaluated at ingest): a message arriving after
    /// this much silence closes the previous session — unanswered challenges expire,
    /// evidenced tickets stay pending for the dispatcher — and opens a new incident.
    /// M2's saga replaces the lazy check with scheduled expiry (tickets expire even
    /// without a next message).
    /// </summary>
    public int SessionInactivityMinutes { get; set; } = 15;

    /// <summary>
    /// Hard cap on a reporter session's age. Without it, regular messages keep
    /// resetting the inactivity clock and a ticket can swallow a conversation forever.
    /// Dispatch targets minutes; an hour-old session is stale by definition.
    /// </summary>
    public int SessionMaxAgeMinutes { get; set; } = 60;

    public double CorridorBufferKm { get; set; } = 2.0;

    /// <summary>
    /// Approximate Berger→Mowe centerline (Lagos–Ibadan Expressway).
    /// TODO(M5): verify waypoints with FRSC Ogun before soft launch.
    /// </summary>
    public GeoPoint[] CorridorCenterline { get; set; } =
    [
        new(6.6435, 3.3655), // Berger interchange
        new(6.6650, 3.3830), // Kara bridge
        new(6.6900, 3.4050), // OPIC
        new(6.7430, 3.4300), // Ibafo
        new(6.8060, 3.4370), // Mowe
    ];
}
