using First10.Domain.Abstractions;
using First10.Domain.Channels;
using First10.Domain.Triage;
using First10.Infrastructure.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace First10.Tests;

public class PerceptualHashTests
{
    private readonly DHashPerceptualHasher _hasher = new();

    private static async Task<Stream> GradientImage(int width, int height, int quality, byte tint = 0)
    {
        using var image = new Image<Rgb24>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Structured, asymmetric content so the dHash is non-trivial.
                var r = (byte)(255 * x / width);
                var g = (byte)(255 * y / height);
                var b = (byte)((x * y) % 251 > 125 ? 200 : 40);
                image[x, y] = new Rgb24((byte)Math.Min(255, r + tint), g, b);
            }
        }
        var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = quality });
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Reencoded_and_resized_image_hashes_near_identical()
    {
        // Simulates WhatsApp re-forwarding: recompressed + downscaled.
        var original = await _hasher.HashAsync(await GradientImage(640, 480, quality: 90), default);
        var forwarded = await _hasher.HashAsync(await GradientImage(320, 240, quality: 55), default);

        Assert.True(PerceptualHash.HammingDistance(original, forwarded) <= 10,
            $"distance was {PerceptualHash.HammingDistance(original, forwarded)}");
    }

    [Fact]
    public async Task Different_content_hashes_far_apart()
    {
        var a = await _hasher.HashAsync(await GradientImage(640, 480, quality: 90), default);

        using var different = new Image<Rgb24>(640, 480);
        for (var y = 0; y < 480; y++)
            for (var x = 0; x < 640; x++)
                different[x, y] = new Rgb24((byte)(255 - 255 * x / 640), (byte)(x % 97), (byte)(255 * y / 480));
        var ms = new MemoryStream();
        await different.SaveAsJpegAsync(ms);
        ms.Position = 0;
        var b = await _hasher.HashAsync(ms, default);

        Assert.True(PerceptualHash.HammingDistance(a, b) > 10,
            $"distance was {PerceptualHash.HammingDistance(a, b)}");
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
