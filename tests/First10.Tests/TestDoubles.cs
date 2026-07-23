using First10.Domain.Abstractions;

namespace First10.Tests;

/// <summary>Shared no-op transcriber: voice notes triage untranscribed (M1 behavior).</summary>
public sealed class TestNullTranscriber : ITranscriber
{
    public Task<string?> TranscribeAsync(Stream audio, string contentType, CancellationToken ct) =>
        Task.FromResult<string?>(null);
}

/// <summary>Shared media-store stub: accepts everything, serves nothing (pHash/extraction skipped).</summary>
public sealed class NullMediaStore : IMediaStore
{
    public Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct) =>
        Task.FromResult("stub.jpg");
    public Task<Stream?> OpenReadAsync(string mediaRef, CancellationToken ct) =>
        Task.FromResult<Stream?>(null);
    public Task DeleteAsync(string mediaRef, CancellationToken ct) => Task.CompletedTask;
    public string GetContentType(string mediaRef) => "image/jpeg";
}
