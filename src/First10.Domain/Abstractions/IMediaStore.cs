namespace First10.Domain.Abstractions;

/// <summary>
/// Opaque media storage (D-012). M1: local filesystem for the dev cockpit.
/// M2 adds the blob-backed implementation + the blur-before-persist gate for real
/// channels (D-009) — the WhatsApp adapter must not land before that gate exists.
/// </summary>
public interface IMediaStore
{
    Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct);

    Task<Stream?> OpenReadAsync(string mediaRef, CancellationToken ct);

    string GetContentType(string mediaRef);
}
