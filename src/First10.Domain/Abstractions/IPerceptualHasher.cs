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
}
