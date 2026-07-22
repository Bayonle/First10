using First10.Domain.Abstractions;
using First10.Domain.Channels;
using First10.Domain.Triage;
using First10.Infrastructure.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace First10.Tests;

public class PerceptualHashTests
{
    private readonly DHashPerceptualHasher _hasher = new();

    /// <summary>
    /// Deterministic textured "scene": an 8×8 grid of seeded random gray blocks in
    /// relative coordinates, so the structure survives any resize — the way real
    /// photo content does. (Smooth gradients are degenerate for dHash — see below.)
    /// </summary>
    private static async Task<Stream> TexturedScene(int seed, int width, int height, int quality)
    {
        const int grid = 8;
        var block = new byte[grid, grid];
        var rng = seed;
        for (var by = 0; by < grid; by++)
        {
            for (var bx = 0; bx < grid; bx++)
            {
                rng = unchecked(rng * 1103515245 + 12345);
                block[bx, by] = (byte)((rng >> 16) & 0xFF);
            }
        }

        using var img = new Image<Rgb24>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var v = block[x * grid / width, y * grid / height];
                img[x, y] = new Rgb24(v, v, v);
            }
        }

        var ms = new MemoryStream();
        await img.SaveAsJpegAsync(ms, new JpegEncoder { Quality = quality });
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Forwarded_copy_hashes_near_original_and_is_not_degenerate()
    {
        // WhatsApp-forward simulation: half resolution, heavy recompression.
        var original = await _hasher.HashAsync(await TexturedScene(42, 640, 480, 90), default);
        var forwarded = await _hasher.HashAsync(await TexturedScene(42, 320, 240, 50), default);

        Assert.False(PerceptualHash.IsDegenerate(original));
        var distance = PerceptualHash.HammingDistance(original, forwarded);
        Assert.True(distance <= 10, $"distance was {distance}");
    }

    [Fact]
    public async Task Different_scenes_hash_far_apart()
    {
        var a = await _hasher.HashAsync(await TexturedScene(42, 640, 480, 90), default);
        var b = await _hasher.HashAsync(await TexturedScene(1337, 640, 480, 90), default);

        var distance = PerceptualHash.HammingDistance(a, b);
        Assert.True(distance > 10, $"distance was {distance}");
    }

    [Fact]
    public async Task Uniform_and_gradient_images_are_degenerate()
    {
        // Live finding (22 Jul): gradients hash to all-ones/all-zeros and collide with
        // every other low-texture image. They must be excluded from reuse detection —
        // a dark night-time crash photo must never be flagged against someone else's.
        using var uniform = new Image<Rgb24>(64, 64);
        var msU = new MemoryStream();
        await uniform.SaveAsJpegAsync(msU);
        msU.Position = 0;
        Assert.True(PerceptualHash.IsDegenerate(await _hasher.HashAsync(msU, default)));

        using var gradient = new Image<Rgb24>(64, 64);
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
                gradient[x, y] = new Rgb24((byte)(x * 4), (byte)(x * 4), (byte)(x * 4));
        var msG = new MemoryStream();
        await gradient.SaveAsJpegAsync(msG);
        msG.Position = 0;
        Assert.True(PerceptualHash.IsDegenerate(await _hasher.HashAsync(msG, default)));
    }

    [Theory]
    [InlineData(0UL, true)]                     // all zeros
    [InlineData(ulong.MaxValue, true)]          // all ones
    [InlineData(0b1011UL, true)]                // 3 bits — still no identity
    [InlineData(0x5555555555555555UL, false)]   // 32 bits — plenty of structure
    public void Degeneracy_thresholds(ulong hash, bool degenerate)
    {
        Assert.Equal(degenerate, PerceptualHash.IsDegenerate(hash));
    }
}

public class CorridorGeofenceTests
{
    private static readonly GeoPoint[] Corridor = new TriageOptions().CorridorCenterline;

    [Theory]
    [InlineData(6.7430, 3.4300)]  // Ibafo, on corridor
    [InlineData(6.6700, 3.3900)]  // between Kara and OPIC
    [InlineData(6.8000, 3.4400)]  // near Mowe
    public void On_corridor_points_pass(double lat, double lng)
    {
        Assert.True(CorridorGeofence.IsNearCorridor(new GeoPoint(lat, lng), Corridor, 2.0));
    }

    [Theory]
    [InlineData(9.0765, 7.3986)]  // Abuja
    [InlineData(6.4550, 3.3841)]  // Lagos Island — off-corridor
    public void Far_points_fail(double lat, double lng)
    {
        Assert.False(CorridorGeofence.IsNearCorridor(new GeoPoint(lat, lng), Corridor, 2.0));
    }
}
