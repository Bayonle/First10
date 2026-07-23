namespace First10.Domain.Abstractions;

/// <summary>How the blur gate resolved an image it could not handle confidently (D-009).</summary>
public enum BlurFallback
{
    /// <summary>Normal path: confident detections blurred (or none present).</summary>
    None = 0,

    /// <summary>Low-confidence detections were blurred with an enlarged region — never ship a maybe-face.</summary>
    ExpandedRegions = 1,

    /// <summary>Detector unavailable or inference failed on a decodable image: the entire frame was blurred.</summary>
    FullFrame = 2,
}

public sealed record BlurResult(
    byte[] BlurredBytes,
    string ContentType,
    int FacesDetected,
    int LowConfidenceRegions,
    double? MinConfidence,
    BlurFallback Fallback,
    long DurationMs);

/// <summary>
/// The D-009 gate. Runs entirely in-process on the raw inbound bytes; the returned
/// bytes are the ONLY form of the image allowed to be persisted, displayed, or sent
/// to any external API. Throws <see cref="NotSupportedException"/> for bytes that
/// cannot be decoded as an image — an image we cannot blur is an image we refuse.
/// </summary>
public interface IFaceBlurrer
{
    Task<BlurResult> BlurAsync(Stream image, CancellationToken ct);
}
