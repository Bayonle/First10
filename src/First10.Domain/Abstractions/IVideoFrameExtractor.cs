namespace First10.Domain.Abstractions;

/// <summary>
/// Extracts still frames from an inbound video inside the ingest scope, so each frame
/// can pass the D-009 blur gate. The raw video is NEVER persisted — frames are the only
/// thing that survives it. Throws <see cref="NotSupportedException"/> when the video
/// cannot be decoded (or no extractor tooling is available): a video we cannot frame
/// is a video we refuse, same contract as an image we cannot blur.
/// </summary>
public interface IVideoFrameExtractor
{
    /// <summary>Returns up to <paramref name="maxFrames"/> frames (PNG bytes), evenly spaced across the duration.</summary>
    Task<IReadOnlyList<byte[]>> ExtractFramesAsync(Stream video, string contentType, int maxFrames, CancellationToken ct);
}
