using System.Diagnostics;
using System.Globalization;
using First10.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace First10.Infrastructure.Media;

/// <summary>
/// ffmpeg-backed frame extraction. The video is spooled to a private temp file (mp4
/// containers are not reliably decodable from a pipe) that is deleted in a finally
/// block before this method returns — it never reaches the media store, and only the
/// blurred derivatives of its frames are ever persisted (D-009). Requires ffmpeg +
/// ffprobe on PATH (dev: `brew install ffmpeg`; pilot VM: `apt install ffmpeg`).
/// </summary>
public sealed class FfmpegVideoFrameExtractor(ILogger<FfmpegVideoFrameExtractor> logger) : IVideoFrameExtractor
{
    public async Task<IReadOnlyList<byte[]>> ExtractFramesAsync(
        Stream video, string contentType, int maxFrames, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"first10-ingest-{Guid.NewGuid():N}.video");
        try
        {
            await using (var spool = File.Create(tempPath))
            {
                await video.CopyToAsync(spool, ct);
            }

            var duration = await ProbeDurationSeconds(tempPath, ct);
            if (duration is null or <= 0)
            {
                throw new NotSupportedException("Video could not be decoded, so its frames cannot be blurred — refusing it.");
            }

            // Evenly spaced sample points, biased off the very first frame (often black).
            var frames = new List<byte[]>();
            for (var i = 0; i < maxFrames; i++)
            {
                var t = duration.Value * (i + 0.5) / maxFrames;
                var frame = await GrabFrame(tempPath, t, ct);
                if (frame is { Length: > 0 })
                {
                    frames.Add(frame);
                }
            }

            if (frames.Count == 0)
            {
                throw new NotSupportedException("No frames could be extracted from the video — refusing it.");
            }

            return frames;
        }
        catch (Exception e) when (e is not NotSupportedException and not OperationCanceledException)
        {
            logger.LogError(e, "ffmpeg frame extraction failed");
            throw new NotSupportedException("Video processing is unavailable — the video was refused, not stored.", e);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* temp dir cleanup will catch strays */ }
        }
    }

    private static async Task<double?> ProbeDurationSeconds(string path, CancellationToken ct)
    {
        var output = await Run("ffprobe",
            $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{path}\"", ct);
        return double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static async Task<byte[]?> GrabFrame(string path, double atSeconds, CancellationToken ct)
    {
        var timestamp = atSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-v error -ss {timestamp} -i \"{path}\" -frames:v 1 -f image2pipe -vcodec png pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new NotSupportedException("ffmpeg could not be started.");

        using var buffer = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(buffer, ct);
        await process.WaitForExitAsync(ct);
        return process.ExitCode == 0 && buffer.Length > 0 ? buffer.ToArray() : null;
    }

    private static async Task<string> Run(string fileName, string arguments, CancellationToken ct)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new NotSupportedException($"{fileName} could not be started.");

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return output;
    }
}
