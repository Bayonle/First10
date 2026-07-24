using First10.Domain.Abstractions;
using First10.Domain.Incidents;
using Microsoft.Extensions.AI;

namespace First10.Infrastructure.Ai;

/// <summary>
/// Multimodal extraction behind IChatClient (D-003/D-014): blurred image + narrative in,
/// structured ticket fields out. Activates when an LLM key is configured; the heuristic
/// extractor covers dev/CI. Selects a template key — never writes clinical text.
/// </summary>
public sealed class ChatIncidentExtractor(IChatClient chatClient) : IIncidentExtractor
{
    public const string Version = "chat-extract-v1";

    private const string SystemPrompt =
        """
        You extract structured crash-report data for FRSC dispatch on the Berger-Mowe
        stretch of the Lagos-Ibadan Expressway. Input: an optional scene photo (faces
        pre-blurred) and the reporter's narrative (English, Nigerian Pidgin, or Yoruba).

        Corridor landmarks (normalize spellings/mis-transcriptions to these):
        Berger interchange, Kara bridge (often heard as "Carra"/"Cara"), Long bridge,
        OPIC estate, Arepo, Warewa, Magboro (Punch flyover), Ibafo, Asese, Redemption
        Camp, Mowe/Ofada. In Pidgin, "motor" means car/vehicle, NOT motorbike;
        a motorcycle is "okada" or "bike".

        Fields:
        - severity: "low" | "medium" | "high". RULE: when uncertain between two tiers,
          choose the HIGHER. Fatalities, trapped victims, fire, tankers, or a person
          not moving are always "high".
        - casualty_estimate: short phrase like "2-3 visible, 1 trapped", or null if
          nothing can be estimated. Never invent counts.
        - template_key: exactly one of "rta_generic" | "rta_fire" | "rta_okada".
          Fire/fuel/tanker involvement -> rta_fire. Motorcycle (okada) -> rta_okada.
          Otherwise rta_generic. You select a pre-approved safety template; you never
          write safety instructions yourself.
        - photo_matches_narrative: false if the photo clearly does not show what the
          narrative describes (unrelated scene). True when plausible or no photo.
        - landmark_key: which corridor landmark the narrative places the incident at,
          exactly one of "berger" | "kara" | "longbridge" | "opic" | "arepo" |
          "warewa" | "magboro" | "ibafo" | "asese" | "redemption" | "mowe", or null.
          You SELECT from this closed list — never guess coordinates, never pick a
          landmark that is not clearly named or implied. Null when unsure.
        - dispatcher_summary: ONE line, <=120 chars, for a corridor dispatcher.
          Lead with what and where. Example: "Trailer/danfo collision at Kara bridge
          inward Lagos, ~3 injured, 1 trapped".
          If the narrative RETRACTS the report (mistake, false alarm, child pressed
          the phone), the summary MUST begin with "REPORTER RETRACTED:" followed by
          the original claim — never quietly blend a retraction into incident facts.
        """;

    public async Task<ExtractionResult> ExtractAsync(ExtractionInput input, CancellationToken ct)
    {
        var userContent = new List<AIContent>();
        if (input.BlurredImage is not null)
        {
            using var buffer = new MemoryStream();
            await input.BlurredImage.CopyToAsync(buffer, ct);
            userContent.Add(new DataContent(buffer.ToArray(), "image/jpeg"));
        }
        userContent.Add(new TextContent(
            $"Narrative ({input.Language}): {input.Narrative ?? "(none — media only)"}"));

        var response = await chatClient.GetResponseAsync<ExtractionWire>(
            [
                new ChatMessage(ChatRole.System, SystemPrompt),
                new ChatMessage(ChatRole.User, userContent),
            ],
            cancellationToken: ct);

        var wire = response.Result;
        // Models sometimes emit the literal strings "null"/"none" instead of JSON null.
        var casualties = wire.CasualtyEstimate?.Trim();
        if (string.IsNullOrWhiteSpace(casualties)
            || casualties.Equals("null", StringComparison.OrdinalIgnoreCase)
            || casualties.Equals("none", StringComparison.OrdinalIgnoreCase)
            || casualties.Equals("n/a", StringComparison.OrdinalIgnoreCase))
        {
            casualties = null;
        }
        return new ExtractionResult(
            MapSeverity(wire.Severity),
            casualties,
            wire.TemplateKey is "rta_fire" or "rta_okada" ? wire.TemplateKey : "rta_generic",
            string.IsNullOrWhiteSpace(wire.DispatcherSummary)
                ? (input.Narrative ?? "RTA report — see timeline")
                : wire.DispatcherSummary,
            Version,
            PhotoMatchesNarrative: wire.PhotoMatchesNarrative ?? true,
            // Closed-list validation: anything not in the gazetteer becomes null.
            LandmarkKey: First10.Domain.Triage.CorridorLandmarks.ByKey(wire.LandmarkKey)?.Key);
    }

    private static SeverityTier MapSeverity(string? value) => value?.ToLowerInvariant() switch
    {
        "low" => SeverityTier.Low,
        "medium" => SeverityTier.Medium,
        // Unknown label → err high (R3).
        _ => SeverityTier.High,
    };

    private sealed record ExtractionWire(
        string? Severity,
        string? CasualtyEstimate,
        string? TemplateKey,
        bool? PhotoMatchesNarrative,
        string? DispatcherSummary,
        string? LandmarkKey);
}
