using System.Text.RegularExpressions;
using First10.Domain.Abstractions;
using First10.Infrastructure.Media;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace First10.Tests;

/// <summary>
/// The D-009 blur gate: detection works on real faces, conservative fallbacks fire,
/// undecodable bytes are refused, and — structurally — no code path can reach
/// IMediaStore.SaveAsync except through SecureMediaIngest.
/// </summary>
public class BlurGateTests
{
    private static string AssetPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", name);

    private static UltraFaceBlurrer CreateBlurrer(string? modelPath = null) => new(
        new BlurOptions { ModelPath = modelPath ?? DefaultModelPath() },
        NullLogger<UltraFaceBlurrer>.Instance);

    private static string DefaultModelPath() => Path.Combine(
        FindRepoRoot(), "src", "First10.Infrastructure", "Media", "Models", "ultraface-RFB-640.onnx");

    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "First10.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found from test base directory.");
    }

    // ---- Detection on real photographs ----

    [Fact]
    public async Task Group_photo_faces_are_detected_and_blurred()
    {
        using var blurrer = CreateBlurrer();
        await using var image = File.OpenRead(AssetPath("faces-group.jpg"));

        var result = await blurrer.BlurAsync(image, default);

        Assert.True(result.FacesDetected >= 3, $"expected several faces, got {result.FacesDetected}");
        Assert.NotEqual(BlurFallback.FullFrame, result.Fallback);
        Assert.True(await MeanDiffExceedsReencode("faces-group.jpg", result.BlurredBytes),
            "blurred output should differ from the original far more than a plain JPEG re-encode");
    }

    [Fact]
    public async Task Single_face_is_detected_and_blurred()
    {
        using var blurrer = CreateBlurrer();
        await using var image = File.OpenRead(AssetPath("face-single.jpg"));

        var result = await blurrer.BlurAsync(image, default);

        Assert.True(result.FacesDetected >= 1, "the single clear face must be detected");
    }

    [Fact]
    public async Task Sceneless_texture_passes_through_with_zero_faces()
    {
        using var blurrer = CreateBlurrer();
        var result = await blurrer.BlurAsync(TexturedNoise(seed: 7), default);

        Assert.Equal(0, result.FacesDetected);
        Assert.Equal(BlurFallback.None, result.Fallback);
        Assert.NotEmpty(result.BlurredBytes); // still re-encoded through the gate
    }

    // ---- Refusal and fallbacks ----

    [Fact]
    public async Task Undecodable_bytes_are_refused_not_persisted()
    {
        using var blurrer = CreateBlurrer();
        using var garbage = new MemoryStream([0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02]);

        await Assert.ThrowsAsync<NotSupportedException>(() => blurrer.BlurAsync(garbage, default));
    }

    [Fact]
    public async Task Missing_model_downgrades_every_image_to_full_frame_blur()
    {
        using var blurrer = CreateBlurrer(modelPath: Path.Combine(Path.GetTempPath(), "no-such-model.onnx"));
        await using var image = File.OpenRead(AssetPath("faces-group.jpg"));

        var result = await blurrer.BlurAsync(image, default);

        Assert.Equal(BlurFallback.FullFrame, result.Fallback);
        Assert.True(await MeanDiffExceedsReencode("faces-group.jpg", result.BlurredBytes),
            "full-frame fallback must visibly transform the whole image");
    }

    [Fact]
    public async Task Warm_blur_meets_the_one_second_receipt_to_blur_target()
    {
        using var blurrer = CreateBlurrer();
        await using (var warmup = File.OpenRead(AssetPath("faces-group.jpg")))
        {
            await blurrer.BlurAsync(warmup, default); // first call pays one-time costs
        }

        await using var image = File.OpenRead(AssetPath("faces-group.jpg"));
        var result = await blurrer.BlurAsync(image, default);

        Assert.True(result.DurationMs <= 1000, $"§7.1 target is ≤1s; took {result.DurationMs}ms");
    }

    // ---- Architecture: the gate cannot be bypassed (D-009) ----

    [Fact]
    public void No_code_path_outside_SecureMediaIngest_can_persist_media()
    {
        var srcRoot = Path.Combine(FindRepoRoot(), "src");
        var allowedFiles = new[] { "SecureMediaIngest.cs" };
        var callPattern = new Regex(@"\.SaveAsync\s*\(");

        var offenders = Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => !allowedFiles.Contains(Path.GetFileName(f)))
            .Where(f => callPattern.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(srcRoot, f))
            .ToList();

        Assert.True(offenders.Count == 0,
            "IMediaStore.SaveAsync may only be called by SecureMediaIngest (the D-009 blur gate). " +
            $"Offending files: {string.Join(", ", offenders)}");
    }

    // ---- Helpers ----

    /// <summary>Deterministic block-noise "scene" — textured like a photo, contains no faces.</summary>
    private static MemoryStream TexturedNoise(int seed)
    {
        const int grid = 8;
        using var img = new Image<Rgb24>(320, 240);
        var rng = seed;
        var block = new byte[grid, grid];
        for (var by = 0; by < grid; by++)
        {
            for (var bx = 0; bx < grid; bx++)
            {
                rng = unchecked(rng * 1103515245 + 12345);
                block[bx, by] = (byte)((rng >> 16) & 0xFF);
            }
        }
        for (var y = 0; y < img.Height; y++)
        {
            for (var x = 0; x < img.Width; x++)
            {
                var v = block[x * grid / img.Width, y * grid / img.Height];
                img[x, y] = new Rgb24(v, v, v);
            }
        }
        var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// True when the blurred output differs from the original clearly more than a plain
    /// JPEG re-encode would — i.e. real pixels were destroyed, not just recompressed.
    /// </summary>
    private static async Task<bool> MeanDiffExceedsReencode(string assetName, byte[] blurredBytes)
    {
        using var original = await Image.LoadAsync<Rgb24>(AssetPath(assetName));
        using var reencoded = await ReencodeAsync(original);
        using var blurred = Image.Load<Rgb24>(blurredBytes);

        var reencodeDiff = MeanAbsDiff(original, reencoded);
        var blurDiff = MeanAbsDiff(original, blurred);
        return blurDiff > reencodeDiff * 1.5 && blurDiff > 1.0;
    }

    private static async Task<Image<Rgb24>> ReencodeAsync(Image<Rgb24> img)
    {
        using var ms = new MemoryStream();
        await img.SaveAsJpegAsync(ms);
        ms.Position = 0;
        return await Image.LoadAsync<Rgb24>(ms);
    }

    private static double MeanAbsDiff(Image<Rgb24> a, Image<Rgb24> b)
    {
        double total = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                total += Math.Abs(a[x, y].R - b[x, y].R)
                       + Math.Abs(a[x, y].G - b[x, y].G)
                       + Math.Abs(a[x, y].B - b[x, y].B);
            }
        }
        return total / (a.Width * a.Height * 3.0);
    }
}
