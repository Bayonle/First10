using First10.Domain.Abstractions;

namespace First10.Tests;

/// <summary>Shared no-op transcriber: voice notes triage untranscribed (M1 behavior).</summary>
public sealed class TestNullTranscriber : ITranscriber
{
    public Task<string?> TranscribeAsync(Stream audio, string contentType, CancellationToken ct) =>
        Task.FromResult<string?>(null);
}
