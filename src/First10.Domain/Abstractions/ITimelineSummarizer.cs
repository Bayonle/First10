namespace First10.Domain.Abstractions;

/// <summary>One timeline event as the summarizer sees it — reporter-anonymous ("Reporter 1").</summary>
public sealed record TimelineSnapshotEntry(
    string Reporter,      // "Reporter 1", "Reporter 2", "system"
    string Kind,          // "text" | "voice(transcript)" | "photo" | "pin" | "note"
    string? Content,
    DateTimeOffset At);

public sealed record TimelineSummaryInput(
    IReadOnlyList<TimelineSnapshotEntry> Entries,
    int ReporterCount,
    string? ResolvedLocation); // "6.66500, 3.38300 (near Kara bridge)" when known

/// <summary>
/// The summarizer's outputs. Contradictions are FIRST-CLASS results (R1f): where
/// witnesses disagree, the disagreement is reported verbatim, never averaged into
/// one confident-sounding line.
/// </summary>
public sealed record TimelineSummary(
    string Digest,
    IReadOnlyList<string> Contradictions,
    string Version);

/// <summary>
/// Reads the WHOLE multi-reporter timeline and answers "what do the witnesses
/// collectively say, and where do they disagree?" — the opposite epistemic job from
/// the extractor, which must commit to one answer. Paper §1.4 relay + §7.1
/// "contradictions flagged: 100%".
/// </summary>
public interface ITimelineSummarizer
{
    Task<TimelineSummary> SummarizeAsync(TimelineSummaryInput input, CancellationToken ct);

    /// <summary>The on-arrival crew briefing, generated at the dispatch action (paper §1.4).</summary>
    Task<string> BriefCrewAsync(TimelineSummaryInput input, CancellationToken ct);
}
