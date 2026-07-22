namespace First10.Domain.Abstractions;

/// <summary>
/// 64-bit perceptual hash for reused-image detection (Stage 0, D-008). WhatsApp strips
/// EXIF, so pHash-vs-seen-corpus is the freshness proxy for re-sent viral crash photos.
/// </summary>
public interface IPerceptualHasher
{
    Task<ulong> HashAsync(Stream image, CancellationToken ct);
}

public static class PerceptualHash
{
    public static int HammingDistance(ulong a, ulong b) => System.Numerics.BitOperations.PopCount(a ^ b);

    /// <summary>
    /// Low-texture images (uniform, smooth gradients, very dark/blurred shots) collapse
    /// into near-all-zero or near-all-one dHashes and collide with each other. Such
    /// hashes carry no identity — never use them for reuse detection (a night-time
    /// crash photo must not be flagged as "reused" against someone else's dark photo).
    /// </summary>
    public static bool IsDegenerate(ulong hash)
    {
        var bits = System.Numerics.BitOperations.PopCount(hash);
        return bits <= 4 || bits >= 60;
    }
}
