namespace First10.Domain.Triage;

public enum MessageIntent
{
    NewIncident = 0,
    IncidentUpdate = 1,
    Question = 2,
    GreetingOrTest = 3,
    SpamOrAbuse = 4,
}

public enum IntentConfidence
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>
/// Stage 1 output (D-008). ClassifierVersion is recorded per ticket so the weekly
/// accuracy review can attribute outcomes to prompt/model versions (M1 task).
/// </summary>
public sealed record IntentResult(
    MessageIntent Intent,
    string Language, // "english" | "pidgin" | "yoruba"
    IntentConfidence Confidence,
    string ClassifierVersion);

/// <summary>
/// Stage 1 contract. Implementations: heuristic (dev/no-key fallback) and
/// LLM-backed behind IChatClient (D-003). Classification is text-only by design —
/// evidence-first routing means images never wait on this call (D-008).
/// </summary>
public interface IIntentClassifier
{
    Task<IntentResult> ClassifyAsync(string text, CancellationToken ct);
}
