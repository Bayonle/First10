using First10.Domain.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace First10.Infrastructure.Media;

/// <summary>
/// Difference hash (dHash): grayscale → 9×8 → 64 adjacent-pixel comparisons.
/// Robust to re-encoding, resizing, and mild recompression — exactly what a
/// re-forwarded WhatsApp image undergoes. Not robust to crops/rotation; good enough
/// for Stage 0, tune threshold via TriageOptions.PerceptualHashThreshold.
/// </summary>
public sealed class DHashPerceptualHasher : IPerceptualHasher
{
    public async Task<ulong> HashAsync(Stream image, CancellationToken ct)
    {
        using var img = await Image.LoadAsync<L8>(image, ct);
        img.Mutate(x => x.Resize(9, 8));

        ulong hash = 0;
        var bit = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                if (img[x + 1, y].PackedValue > img[x, y].PackedValue)
                {
                    hash |= 1UL << bit;
                }
                bit++;
            }
        }
        return hash;
    }
}
