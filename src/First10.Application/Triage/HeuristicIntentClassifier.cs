using System.Globalization;
using System.Text;
using First10.Domain.Triage;

namespace First10.Application.Triage;

/// <summary>
/// Keyword-based Stage 1 fallback: active whenever no LLM key is configured (dev,
/// tests, CI, scenario runs). Deliberately conservative — the real classifier is the
/// LLM one (D-003); this exists so the funnel is exercisable end-to-end offline.
/// </summary>
public sealed class HeuristicIntentClassifier : IIntentClassifier
{
    public const string Version = "heuristic-v1";

    private static readonly string[] StrongIncidentWords =
    [
        // english
        "accident", "crash", "collision", "collide", "overturn", "somersault", "trapped",
        "bleeding", "casualty", "casualties", "hit and run",
        // pidgin
        "don jam", "don crash", "gbas gbos", "tumble", "scatter",
        // yoruba (diacritics stripped before matching; "ìjàǹbá" → "ijanba")
        "ijanba", "ijamba", "ajalu", "farapa", "jamba",
    ];

    private static readonly string[] SupportingWords =
    [
        "okada", "danfo", "trailer", "tanker", "lorry", "bus", "express", "expressway",
        "blood", "injured", "injury", "wound", "die", "dead", "help", "emergency",
        "ambulance", "fire", "burn", "victim", "eje", "wahala",
    ];

    private static readonly string[] GreetingWords =
        ["hi", "hello", "hey", "test", "testing", "good morning", "good afternoon", "good evening"];

    private static readonly string[] SpamMarkers =
        ["http://", "https://", "www.", "promo", "giveaway", "win big", "jackpot", "bet now", "odds"];

    private static readonly string[] PidginMarkers =
        [" dey ", " don ", " na ", "abeg", "wahala", " wey ", "oga", " o!", " oo"];

    private static readonly string[] YorubaMarkers =
        ["ijanba", "ijamba", "ajalu", "farapa", "jowo", "egba mi", "sele", "ti sele", "kiakia"];

    public Task<IntentResult> ClassifyAsync(string text, CancellationToken ct)
    {
        var normalized = Normalize(text);
        var language = DetectLanguage(normalized);

        var strongHits = StrongIncidentWords.Count(normalized.Contains);
        var supportingHits = SupportingWords.Count(normalized.Contains);

        MessageIntent intent;
        IntentConfidence confidence;

        if (strongHits > 0)
        {
            intent = MessageIntent.NewIncident;
            confidence = strongHits + supportingHits >= 2 ? IntentConfidence.High : IntentConfidence.Medium;
        }
        else if (supportingHits >= 2)
        {
            // No explicit "accident" word but multiple crash-adjacent signals — bias
            // toward incident (D-008): later stages absorb false alarms.
            intent = MessageIntent.NewIncident;
            confidence = IntentConfidence.Low;
        }
        else if (SpamMarkers.Any(normalized.Contains))
        {
            intent = MessageIntent.SpamOrAbuse;
            confidence = IntentConfidence.Medium;
        }
        else if (GreetingWords.Any(g => normalized.Trim() == g || normalized.StartsWith(g + " ") || normalized.StartsWith(g + "!")))
        {
            intent = MessageIntent.GreetingOrTest;
            confidence = IntentConfidence.Medium;
        }
        else
        {
            intent = MessageIntent.Question;
            confidence = IntentConfidence.Low;
        }

        return Task.FromResult(new IntentResult(intent, language, confidence, Version));
    }

    private static string DetectLanguage(string normalized)
    {
        if (YorubaMarkers.Any(normalized.Contains))
        {
            return "yoruba";
        }
        return PidginMarkers.Any(normalized.Contains) ? "pidgin" : "english";
    }

    /// <summary>Lowercase + strip diacritics so "ìjàǹbá" matches "ijamba".</summary>
    private static string Normalize(string text)
    {
        var formD = (" " + text.ToLowerInvariant() + " ").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
