using First10.Domain.Abstractions;
using OpenAI.Audio;

namespace First10.Infrastructure.Ai;

/// <summary>
/// Whisper STT (D-010). Registered only when an OpenAI key is configured; the
/// NullTranscriber covers dev/CI. Corridor languages: Whisper handles English and
/// Yoruba; Pidgin transcribes as English-adjacent text the classifier normalizes.
/// M5 gate: accuracy spot-check on real corridor voice notes before soft launch.
/// </summary>
public sealed class WhisperTranscriber(AudioClient audioClient) : ITranscriber
{
    private static readonly Dictionary<string, string> ExtensionByContentType = new()
    {
        ["audio/webm"] = "voice.webm",
        ["audio/ogg"] = "voice.ogg",
        ["audio/mp4"] = "voice.m4a",
        ["audio/mpeg"] = "voice.mp3",
    };

    public async Task<string?> TranscribeAsync(Stream audio, string contentType, CancellationToken ct)
    {
        var fileName = ExtensionByContentType.GetValueOrDefault(contentType, "voice.webm");
        var result = await audioClient.TranscribeAudioAsync(audio, fileName, cancellationToken: ct);
        var text = result.Value.Text?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}

/// <summary>No STT configured: voice notes triage as low-confidence incidents (M1 behavior).</summary>
public sealed class NullTranscriber : ITranscriber
{
    public Task<string?> TranscribeAsync(Stream audio, string contentType, CancellationToken ct) =>
        Task.FromResult<string?>(null);
}
