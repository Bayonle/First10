using Amazon.S3;
using Amazon.S3.Model;
using First10.Domain.Abstractions;

namespace First10.Infrastructure.Media;

/// <summary>
/// S3-compatible media store (D-012/D-016): MinIO in development (Aspire container),
/// any S3-compatible target in production. Refs stay opaque "{guid}{ext}" keys.
/// Encryption-at-rest and access-signed serving are M4 concerns layered on top.
/// </summary>
public sealed class S3MediaStore(IAmazonS3 s3, string bucket) : IMediaStore
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

    private readonly SemaphoreSlim _bucketGate = new(1, 1);
    private bool _bucketEnsured;

    public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct)
    {
        if (!ExtensionByContentType.TryGetValue(contentType, out var extension))
        {
            throw new NotSupportedException($"Unsupported media content type '{contentType}'.");
        }

        await EnsureBucket(ct);

        var mediaRef = $"{Guid.NewGuid():N}{extension}";
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = mediaRef,
            InputStream = content,
            ContentType = contentType,
        }, ct);
        return mediaRef;
    }

    public async Task<Stream?> OpenReadAsync(string mediaRef, CancellationToken ct)
    {
        if (Path.GetFileName(mediaRef) != mediaRef || string.IsNullOrWhiteSpace(mediaRef))
        {
            throw new ArgumentException("Invalid media ref.", nameof(mediaRef));
        }

        try
        {
            using var response = await s3.GetObjectAsync(bucket, mediaRef, ct);
            // Buffer so the response can be disposed deterministically. Pilot media is
            // capped at 15MB (upload limit); revisit with streaming if that changes.
            var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            return buffer;
        }
        catch (AmazonS3Exception e) when (e.ErrorCode is "NoSuchKey" or "NoSuchBucket")
        {
            return null;
        }
    }

    public async Task DeleteAsync(string mediaRef, CancellationToken ct)
    {
        if (Path.GetFileName(mediaRef) != mediaRef || string.IsNullOrWhiteSpace(mediaRef))
        {
            throw new ArgumentException("Invalid media ref.", nameof(mediaRef));
        }

        try
        {
            await s3.DeleteObjectAsync(bucket, mediaRef, ct); // S3 delete is idempotent by contract
        }
        catch (AmazonS3Exception e) when (e.ErrorCode is "NoSuchBucket")
        {
            // nothing to delete
        }
    }

    public string GetContentType(string mediaRef) =>
        ContentTypeByExtension.GetValueOrDefault(Path.GetExtension(mediaRef), "application/octet-stream");

    private async Task EnsureBucket(CancellationToken ct)
    {
        if (_bucketEnsured) return;
        await _bucketGate.WaitAsync(ct);
        try
        {
            if (_bucketEnsured) return;
            try
            {
                await s3.PutBucketAsync(bucket, ct);
            }
            catch (AmazonS3Exception e) when (e.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
            {
                // fine — someone (or a previous run) created it
            }
            _bucketEnsured = true;
        }
        finally
        {
            _bucketGate.Release();
        }
    }
}
