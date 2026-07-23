using First10.Domain.Abstractions;
using First10.Domain.Triage;
using Microsoft.Extensions.Logging;

namespace First10.Application.Triage;

/// <summary>
/// LLM resilience decorators (D-008: a provider blip must never lose a report). The M4
/// load test dead-lettered messages on OpenAI 429s/timeouts and malformed structured
/// output — with these wrappers, any LLM failure degrades to the heuristic result
/// (severity errs high, voice goes to Review, text gets keyword triage) and the
/// pipeline keeps moving. Every fallback is logged: quality degradation stays visible.
/// </summary>
public sealed class ResilientIntentClassifier(
    IIntentClassifier primary, IIntentClassifier fallback, ILogger<ResilientIntentClassifier> logger)
    : IIntentClassifier
{
    public async Task<IntentResult> ClassifyAsync(string text, CancellationToken ct)
    {
        try
        {
            return await primary.ClassifyAsync(text, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "LLM intent classification failed — heuristic fallback");
            return await fallback.ClassifyAsync(text, ct);
        }
    }
}

public sealed class ResilientIncidentExtractor(
    IIncidentExtractor primary, IIncidentExtractor fallback, ILogger<ResilientIncidentExtractor> logger)
    : IIncidentExtractor
{
    public async Task<ExtractionResult> ExtractAsync(ExtractionInput input, CancellationToken ct)
    {
        try
        {
            return await primary.ExtractAsync(input, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "LLM extraction failed — heuristic fallback");
            return await fallback.ExtractAsync(input, ct);
        }
    }
}

public sealed class ResilientTimelineSummarizer(
    ITimelineSummarizer primary, ITimelineSummarizer fallback, ILogger<ResilientTimelineSummarizer> logger)
    : ITimelineSummarizer
{
    public async Task<TimelineSummary> SummarizeAsync(TimelineSummaryInput input, CancellationToken ct)
    {
        try
        {
            return await primary.SummarizeAsync(input, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "LLM summarization failed — heuristic fallback");
            return await fallback.SummarizeAsync(input, ct);
        }
    }

    public async Task<string> BriefCrewAsync(TimelineSummaryInput input, CancellationToken ct)
    {
        try
        {
            return await primary.BriefCrewAsync(input, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "LLM crew briefing failed — heuristic fallback");
            return await fallback.BriefCrewAsync(input, ct);
        }
    }
}

/// <summary>STT failure ⇒ null transcript: the voice note still triages (dispatcher's ear decides).</summary>
public sealed class ResilientTranscriber(ITranscriber primary, ILogger<ResilientTranscriber> logger)
    : ITranscriber
{
    public async Task<string?> TranscribeAsync(Stream audio, string contentType, CancellationToken ct)
    {
        try
        {
            return await primary.TranscribeAsync(audio, contentType, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "STT failed — voice note proceeds untranscribed");
            return null;
        }
    }
}
