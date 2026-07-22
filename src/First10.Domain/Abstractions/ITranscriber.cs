namespace First10.Domain.Abstractions;

/// <summary>
/// Speech-to-text for voice notes (D-010: Whisper in pilot). Null result = could not
/// transcribe (no STT configured, or unintelligible audio) — the pipeline treats such
/// voice notes as low-confidence incident reports and the dispatcher's ear decides.
/// </summary>
public interface ITranscriber
{
    Task<string?> TranscribeAsync(Stream audio, string contentType, CancellationToken ct);
}
