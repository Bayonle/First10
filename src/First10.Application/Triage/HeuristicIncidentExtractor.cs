using First10.Domain.Abstractions;
using First10.Domain.Triage;
using First10.Domain.Incidents;

namespace First10.Application.Triage;

/// <summary>
/// Keyword extraction fallback — active when no LLM key is configured (dev/CI).
/// Deliberately conservative: severity errs HIGH on strong signals and never
/// downgrades below Medium on uncertainty (R3). The chat extractor replaces this
/// for the pilot (D-003/D-014).
/// </summary>
public sealed class HeuristicIncidentExtractor : IIncidentExtractor
{
    public const string Version = "heuristic-extract-v1";

    private static readonly string[] HighSeverityWords =
        ["trapped", "die", "dead", "iku", "fire", "burn", "blood", "eje", "bleeding",
         "tanker", "unconscious", "no dey move", "no fit move", "somersault"];

    private static readonly string[] FireWords = ["fire", "burn", "smoke", "tanker"];
    private static readonly string[] OkadaWords = ["okada", "bike", "motorcycle"];

    public Task<ExtractionResult> ExtractAsync(ExtractionInput input, CancellationToken ct)
    {
        var text = (input.Narrative ?? string.Empty).ToLowerInvariant();

        var severity = HighSeverityWords.Any(text.Contains)
            ? SeverityTier.High
            : SeverityTier.Medium; // uncertain → never Low (R3)

        var templateKey = FireWords.Any(text.Contains) ? "rta_fire"
            : OkadaWords.Any(text.Contains) ? "rta_okada"
            : "rta_generic";

        var summary = string.IsNullOrWhiteSpace(input.Narrative)
            ? "RTA report (media only) — see timeline"
            : input.Narrative.Length <= 140 ? input.Narrative : input.Narrative[..140];

        return Task.FromResult(new ExtractionResult(
            severity,
            CasualtyEstimate: null, // keyword guessing casualty counts would be noise
            templateKey,
            summary,
            Version,
            LandmarkKey: CorridorLandmarks.Match(input.Narrative)?.Key));
    }
}
