using First10.Domain.Triage;
using Microsoft.Extensions.AI;

namespace First10.Infrastructure.Ai;

/// <summary>
/// Stage 1 behind IChatClient (D-003) — provider-agnostic; the pilot wires an OpenAI
/// client in DI. Structured output keeps the funnel parse-failure-free.
/// </summary>
public sealed class ChatIntentClassifier(IChatClient chatClient) : IIntentClassifier
{
    public const string Version = "chat-v1";

    private const string SystemPrompt =
        """
        You classify WhatsApp messages sent to First10, a road-crash reporting line for the
        Berger-Mowe stretch of the Lagos-Ibadan Expressway in Nigeria. Messages arrive in
        English, Nigerian Pidgin, or Yoruba, often typed in panic with poor spelling.

        Classify the message intent:
        - new_incident: reporting a road traffic crash happening now or just witnessed
        - incident_update: additional detail about a crash ("the trailer don catch fire")
        - question: asking something ("which number be this?", "how una dey work?")
        - greeting_or_test: greetings, "test", checking if the line works
        - spam_or_abuse: promotions, links, betting, harassment, nonsense

        RULES:
        1. When uncertain between new_incident and anything else, choose new_incident.
           A missed real crash is unrecoverable; a false alarm costs a reviewer seconds.
        2. Detect the language: english, pidgin, or yoruba.
        3. Examples:
           "Accident dey happen for Mowe o! Two okada down" -> new_incident, pidgin, high
           "Ijamba ti sele ni Ibafo, e ran wa lowo" -> new_incident, yoruba, high
           "Trailer don fall for Kara bridge" -> new_incident, pidgin, high
           "There is a bad crash before the toll gate, people are trapped" -> new_incident, english, high
           "Person just somersault with bike near OPIC" -> new_incident, pidgin, high
           "How far, which number be this" -> question, pidgin, medium
           "Good morning" -> greeting_or_test, english, high
           "WIN BIG! Play now www.bet.example" -> spam_or_abuse, english, high
        """;

    public async Task<IntentResult> ClassifyAsync(string text, CancellationToken ct)
    {
        var response = await chatClient.GetResponseAsync<IntentClassification>(
            [
                new ChatMessage(ChatRole.System, SystemPrompt),
                new ChatMessage(ChatRole.User, text),
            ],
            cancellationToken: ct);

        var result = response.Result;
        return new IntentResult(
            MapIntent(result.Intent),
            result.Language?.ToLowerInvariant() is "pidgin" or "yoruba" ? result.Language.ToLowerInvariant() : "english",
            MapConfidence(result.Confidence),
            Version);
    }

    private static MessageIntent MapIntent(string? value) => value?.ToLowerInvariant() switch
    {
        "new_incident" => MessageIntent.NewIncident,
        "incident_update" => MessageIntent.IncidentUpdate,
        "question" => MessageIntent.Question,
        "greeting_or_test" => MessageIntent.GreetingOrTest,
        "spam_or_abuse" => MessageIntent.SpamOrAbuse,
        // Unknown label -> bias toward incident (rule 1).
        _ => MessageIntent.NewIncident,
    };

    private static IntentConfidence MapConfidence(string? value) => value?.ToLowerInvariant() switch
    {
        "high" => IntentConfidence.High,
        "medium" => IntentConfidence.Medium,
        _ => IntentConfidence.Low,
    };

    /// <summary>Wire schema for structured output — string-typed so the model sees enum names.</summary>
    private sealed record IntentClassification(string? Intent, string? Language, string? Confidence);
}
