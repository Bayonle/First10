using System.Diagnostics;
using First10.Domain.Abstractions;
using First10.Infrastructure.Media;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace First10.Tests;

/// <summary>
/// D-019 video path: the gate's content-type policy is closed (unknown types refused at
/// the gate, not by store coincidence), video frames are extracted in-scope and blurred
/// individually, and only a blurred contact-sheet IMAGE is ever persisted — the raw
/// video never reaches the store. ffmpeg-dependent tests skip when ffmpeg is absent.
/// </summary>
public class VideoIngestTests
{
    // ---- Gate policy (no ffmpeg needed) ----

    /// <summary>Accepts everything — proves refusals below come from the GATE's policy.</summary>
    private sealed class PermissiveCapturingStore : IMediaStore
    {
        public List<(string ContentType, byte[] Bytes)> Saved { get; } = [];

        public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            Saved.Add((contentType, ms.ToArray()));
            return $"saved-{Saved.Count}.bin";
        }

        public Task<Stream?> OpenReadAsync(string mediaRef, CancellationToken ct) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string mediaRef, CancellationToken ct) => Task.CompletedTask;
        public string GetContentType(string mediaRef) => "application/octet-stream";
    }

    private sealed class NoopFrameExtractor : IVideoFrameExtractor
    {
        public Task<IReadOnlyList<byte[]>> ExtractFramesAsync(Stream video, string contentType, int maxFrames, CancellationToken ct) =>
            throw new NotSupportedException("no extractor in this test");
    }

    private static First10DbContext NewDb() =>
        new(new DbContextOptionsBuilder<First10DbContext>()
            .UseInMemoryDatabase($"first10-{Guid.NewGuid()}")
            .Options);

    private static UltraFaceBlurrer RealBlurrer() => new(
        new BlurOptions
        {
            ModelPath = Path.Combine(BlurGateTests.FindRepoRoot(),
                "src", "First10.Infrastructure", "Media", "Models", "ultraface-RFB-640.onnx"),
        },
        NullLogger<UltraFaceBlurrer>.Instance);

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/octet-stream")]
    [InlineData("text/html")]
    [InlineData("audio/x-obscure-codec")] // not on the audio whitelist
    public async Task Unknown_content_types_are_refused_by_the_gate_even_with_a_permissive_store(string contentType)
    {
        var store = new PermissiveCapturingStore();
        await using var db = NewDb();
        using var blurrer = RealBlurrer();
        var gate = new SecureMediaIngest(store, blurrer, new NoopFrameExtractor(), db);

        using var content = new MemoryStream([1, 2, 3, 4]);
        await Assert.ThrowsAsync<NotSupportedException>(() => gate.IngestAsync(content, contentType, default));
        Assert.Empty(store.Saved); // nothing reached persistence
    }

    [Fact]
    public async Task Video_is_refused_outright_when_frame_extraction_is_unavailable()
    {
        var store = new PermissiveCapturingStore();
        await using var db = NewDb();
        using var blurrer = RealBlurrer();
        var gate = new SecureMediaIngest(store, blurrer, new NoopFrameExtractor(), db);

        using var content = new MemoryStream([0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70]); // mp4-ish header
        await Assert.ThrowsAsync<NotSupportedException>(() => gate.IngestAsync(content, "video/mp4", default));
        Assert.Empty(store.Saved); // an unprocessable video is never stored raw
    }

    // ---- ffmpeg-backed extraction + end-to-end (skip when ffmpeg is absent) ----

    private static bool FfmpegAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg", Arguments = "-version",
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            });
            p!.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>4s test video looping the group-faces photo — every frame contains 50+ faces.</summary>
    private static async Task<string> GenerateFacesVideo()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Assets", "faces-group.jpg");
        var output = Path.Combine(Path.GetTempPath(), $"first10-test-{Guid.NewGuid():N}.mp4");
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-v error -loop 1 -i \"{source}\" -t 4 -r 10 -c:v libx264 -pix_fmt yuv420p -vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" \"{output}\"",
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        });
        await p!.WaitForExitAsync();
        Assert.True(p.ExitCode == 0, "test video generation failed");
        return output;
    }

    [Fact]
    public async Task Extractor_returns_evenly_spaced_decodable_frames()
    {
        if (!FfmpegAvailable()) return; // environment without ffmpeg — covered on dev/pilot machines

        var videoPath = await GenerateFacesVideo();
        try
        {
            var extractor = new FfmpegVideoFrameExtractor(NullLogger<FfmpegVideoFrameExtractor>.Instance);
            await using var video = File.OpenRead(videoPath);

            var frames = await extractor.ExtractFramesAsync(video, "video/mp4", 4, default);

            Assert.Equal(4, frames.Count);
            foreach (var frame in frames)
            {
                using var img = Image.Load<Rgb24>(frame); // decodable
                Assert.True(img.Width > 100);
            }
        }
        finally
        {
            File.Delete(videoPath);
        }
    }

    [Fact]
    public async Task Video_with_faces_becomes_a_single_blurred_contact_sheet_and_the_raw_video_is_never_stored()
    {
        if (!FfmpegAvailable()) return;

        var videoPath = await GenerateFacesVideo();
        try
        {
            var store = new PermissiveCapturingStore();
            await using var db = NewDb();
            using var blurrer = RealBlurrer();
            var gate = new SecureMediaIngest(
                store, blurrer, new FfmpegVideoFrameExtractor(NullLogger<FfmpegVideoFrameExtractor>.Instance), db);

            await using var video = File.OpenRead(videoPath);
            var result = await gate.IngestAsync(video, "video/mp4", default);

            // Exactly one artifact persisted, and it is an IMAGE (the contact sheet).
            var saved = Assert.Single(store.Saved);
            Assert.Equal("image/jpeg", saved.ContentType);

            // The sheet decodes and is a 2x2 grid of the frames.
            using var sheet = Image.Load<Rgb24>(saved.Bytes);
            Assert.True(sheet.Width > 1200, $"expected two 640px columns, got {sheet.Width}px");

            // Faces were found and blurred in the frames (each frame holds 50+).
            Assert.NotNull(result.Blur);
            Assert.True(result.Blur!.FacesDetected >= 40,
                $"expected many faces across 4 frames, got {result.Blur.FacesDetected}");

            // Audit row written for the sheet.
            Assert.Single(db.BlurAudits.Where(a => a.MediaRef == result.MediaRef));
        }
        finally
        {
            File.Delete(videoPath);
        }
    }
}
