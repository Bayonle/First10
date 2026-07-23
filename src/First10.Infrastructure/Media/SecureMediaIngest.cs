using First10.Domain.Abstractions;
using First10.Domain.Incidents;
using First10.Infrastructure.Persistence;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace First10.Infrastructure.Media;

public sealed record MediaIngestResult(string MediaRef, BlurResult? Blur);

/// <summary>
/// The ONLY legal route from inbound media bytes to persistence (D-009). Every channel
/// adapter — Local cockpit today, WhatsApp/Telegram in M5 — hands raw downloaded bytes
/// here. The content-type policy is explicit and closed:
///
///   image/*          → face-blur → store
///   audio whitelist  → passthrough (no faces; voice handled under D-013 retention)
///   video/*          → frames extracted in-scope → EACH frame face-blurred → blurred
///                      frames composited into one contact-sheet image → store the
///                      sheet; the raw video is never persisted
///   anything else    → refused (NotSupportedException)
///
/// The video and unknown-type rules live HERE, not in the stores' extension maps —
/// adding a content type to a store must never be able to open an unblurred path.
/// An architecture test pins this class as the only IMediaStore.SaveAsync caller.
/// </summary>
public sealed class SecureMediaIngest(
    IMediaStore store, IFaceBlurrer blurrer, IVideoFrameExtractor videoFrames, First10DbContext db)
{
    private const int MaxVideoFrames = 4;

    private static readonly HashSet<string> AudioPassthrough = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/webm", "audio/ogg", "audio/mp4", "audio/mpeg",
    };

    public async Task<MediaIngestResult> IngestAsync(Stream content, string contentType, CancellationToken ct)
    {
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            var blur = await blurrer.BlurAsync(content, ct); // throws for undecodable bytes
            return await StoreBlurred(blur, ct);
        }

        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return await IngestVideo(content, contentType, ct);
        }

        if (AudioPassthrough.Contains(contentType))
        {
            var passthroughRef = await store.SaveAsync(content, contentType, ct);
            return new MediaIngestResult(passthroughRef, null);
        }

        // Closed-world default: a type we have no safe handling for is refused, never
        // stored. This is the gate's rule — a store extension map cannot override it.
        throw new NotSupportedException($"Unsupported media content type '{contentType}'.");
    }

    /// <summary>
    /// Video → blurred contact sheet. Frames are blurred INDIVIDUALLY (full detector
    /// resolution per frame — compositing first would shrink faces below detectability)
    /// and only then tiled into a single image, so the sheet inherits the same D-009
    /// guarantee as any photo. One mediaRef out = the rest of the pipeline (triage
    /// ceilings, pHash, extraction, console, retention) treats it exactly like a photo.
    /// </summary>
    private async Task<MediaIngestResult> IngestVideo(Stream content, string contentType, CancellationToken ct)
    {
        var frames = await videoFrames.ExtractFramesAsync(content, contentType, MaxVideoFrames, ct);

        var blurredFrames = new List<BlurResult>(frames.Count);
        foreach (var frame in frames)
        {
            using var frameStream = new MemoryStream(frame);
            blurredFrames.Add(await blurrer.BlurAsync(frameStream, ct));
        }

        var sheet = ComposeContactSheet(blurredFrames);
        var combined = new BlurResult(
            sheet,
            "image/jpeg",
            blurredFrames.Sum(b => b.FacesDetected),
            blurredFrames.Sum(b => b.LowConfidenceRegions),
            blurredFrames.Min(b => b.MinConfidence),
            blurredFrames.Max(b => b.Fallback), // worst frame's fallback describes the sheet
            blurredFrames.Sum(b => b.DurationMs));

        return await StoreBlurred(combined, ct);
    }

    private async Task<MediaIngestResult> StoreBlurred(BlurResult blur, CancellationToken ct)
    {
        using var blurred = new MemoryStream(blur.BlurredBytes);
        var mediaRef = await store.SaveAsync(blurred, blur.ContentType, ct);

        db.BlurAudits.Add(new BlurAuditRecord
        {
            Id = Guid.NewGuid(),
            MediaRef = mediaRef,
            FacesDetected = blur.FacesDetected,
            LowConfidenceRegions = blur.LowConfidenceRegions,
            MinConfidence = blur.MinConfidence,
            Fallback = blur.Fallback,
            DurationMs = blur.DurationMs,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return new MediaIngestResult(mediaRef, blur);
    }

    /// <summary>2-across grid of already-blurred frames, each scaled to a uniform cell width.</summary>
    private static byte[] ComposeContactSheet(IReadOnlyList<BlurResult> blurredFrames)
    {
        const int cellWidth = 640;
        const int gutter = 4;

        var tiles = blurredFrames
            .Select(b => Image.Load<Rgb24>(b.BlurredBytes))
            .Select(img =>
            {
                var height = (int)Math.Round(img.Height * (cellWidth / (double)img.Width));
                img.Mutate(x => x.Resize(cellWidth, Math.Max(1, height)));
                return img;
            })
            .ToList();

        try
        {
            if (tiles.Count == 1)
            {
                using var single = new MemoryStream();
                tiles[0].SaveAsJpeg(single);
                return single.ToArray();
            }

            var columns = 2;
            var rows = (tiles.Count + columns - 1) / columns;
            var rowHeights = Enumerable.Range(0, rows)
                .Select(r => tiles.Skip(r * columns).Take(columns).Max(t => t.Height))
                .ToArray();

            using var sheet = new Image<Rgb24>(
                columns * cellWidth + (columns - 1) * gutter,
                rowHeights.Sum() + (rows - 1) * gutter,
                new Rgb24(24, 24, 24));

            var y = 0;
            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < columns; c++)
                {
                    var index = r * columns + c;
                    if (index >= tiles.Count) break;
                    var position = new Point(c * (cellWidth + gutter), y);
                    sheet.Mutate(x => x.DrawImage(tiles[index], position, 1f));
                }
                y += rowHeights[r] + gutter;
            }

            using var output = new MemoryStream();
            sheet.SaveAsJpeg(output);
            return output.ToArray();
        }
        finally
        {
            foreach (var tile in tiles) tile.Dispose();
        }
    }
}
