using First10.Domain.Incidents;

namespace First10.Domain.Abstractions;

/// <summary>
/// Extraction input: whatever the session holds so far. Image is the BLURRED bytes
/// (D-009 — nothing unblurred ever reaches an extractor, local or remote).
/// </summary>
public sealed record ExtractionInput(
    string? Narrative,          // text messages + voice transcripts, concatenated
    Stream? BlurredImage,
    string Language);

/// <summary>
/// Extraction output. TemplateKey SELECTS a clinically approved micro-instruction —
/// extraction never writes clinical text (D-011/D-014). Severity errs high when
/// uncertain (R3).
/// </summary>
public sealed record ExtractionResult(
    SeverityTier Severity,
    string? CasualtyEstimate,
    string TemplateKey,
    string DispatcherSummary,
    string ExtractorVersion,
    /// <summary>False when the photo clearly does not show what the narrative claims —
    /// caps disposition at Review + flags, never drops (D-008).</summary>
    bool PhotoMatchesNarrative = true,
    /// <summary>Corridor gazetteer key SELECTED from the closed landmark list when the
    /// narrative names a place ("accident for Kara bridge") — never invented
    /// coordinates. Null when no landmark is confidently named.</summary>
    string? LandmarkKey = null);

public interface IIncidentExtractor
{
    Task<ExtractionResult> ExtractAsync(ExtractionInput input, CancellationToken ct);
}
