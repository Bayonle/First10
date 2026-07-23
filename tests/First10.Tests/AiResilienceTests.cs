using First10.Application.Triage;
using First10.Domain.Triage;
using Microsoft.Extensions.Logging.Abstractions;

namespace First10.Tests;

/// <summary>
/// D-008: an LLM provider failure (429, timeout, malformed structured output) degrades
/// to the heuristic result — it never throws out of the ingest handler, because a thrown
/// exception there becomes a dead-lettered (silently lost) report.
/// </summary>
public class AiResilienceTests
{
    private sealed class ExplodingClassifier : IIntentClassifier
    {
        public Task<IntentResult> ClassifyAsync(string text, CancellationToken ct) =>
            throw new HttpRequestException("429 Too Many Requests");
    }

    [Fact]
    public async Task Classifier_falls_back_to_heuristics_when_the_llm_fails()
    {
        var resilient = new ResilientIntentClassifier(
            new ExplodingClassifier(), new HeuristicIntentClassifier(),
            NullLogger<ResilientIntentClassifier>.Instance);

        var result = await resilient.ClassifyAsync("accident dey happen for kara bridge o", default);

        Assert.Equal(MessageIntent.NewIncident, result.Intent); // heuristic keyword triage
        Assert.NotEqual("chat-v1", result.ClassifierVersion);   // provably the fallback
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed_as_a_fallback()
    {
        var resilient = new ResilientIntentClassifier(
            new CancelledClassifier(), new HeuristicIntentClassifier(),
            NullLogger<ResilientIntentClassifier>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => resilient.ClassifyAsync("anything", default));
    }

    private sealed class CancelledClassifier : IIntentClassifier
    {
        public Task<IntentResult> ClassifyAsync(string text, CancellationToken ct) =>
            throw new OperationCanceledException();
    }
}
