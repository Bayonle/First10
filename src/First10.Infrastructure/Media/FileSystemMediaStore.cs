using First10.Domain.Abstractions;

namespace First10.Infrastructure.Media;

/// <summary>
/// M1 media store: local filesystem, for the dev cockpit only. The blob-backed store
/// with encryption + access logging replaces this for real channels (M2/M4, D-012).
/// Media refs are opaque "{guid}{ext}" strings — no path components accepted back.
/// </summary>
public sealed class FileSystemMediaStore(string rootPath) : IMediaStore
{
    private static readonly Dictionary<string, string> ExtensionByContentType = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["audio/webm"] = ".webm",
        ["audio/ogg"] = ".ogg",
        ["audio/mp4"] = ".m4a",
        ["audio/mpeg"] = ".mp3",
    };

    private static readonly Dictionary<string, string> ContentTypeByExtension =
        ExtensionByContentType.ToDictionary(kv => kv.Value, kv => kv.Key);

    public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct)
    {
        if (!ExtensionByContentType.TryGetValue(contentType, out var extension))
        {
            throw new NotSupportedException($"Unsupported media content type '{contentType}'.");
        }

        Directory.CreateDirectory(rootPath);
        var mediaRef = $"{Guid.NewGuid():N}{extension}";
        await using var file = File.Create(Path.Combine(rootPath, mediaRef));
        await content.CopyToAsync(file, ct);
        return mediaRef;
    }

    public Task<Stream?> OpenReadAsync(string mediaRef, CancellationToken ct)
    {
        var path = Resolve(mediaRef);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    public string GetContentType(string mediaRef) =>
        ContentTypeByExtension.GetValueOrDefault(Path.GetExtension(mediaRef), "application/octet-stream");

    private string Resolve(string mediaRef)
    {
        // Defense in depth: refs are opaque filenames we minted; reject anything path-like.
        var fileName = Path.GetFileName(mediaRef);
        if (fileName != mediaRef || string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Invalid media ref.", nameof(mediaRef));
        }
        return Path.Combine(rootPath, fileName);
    }
}
