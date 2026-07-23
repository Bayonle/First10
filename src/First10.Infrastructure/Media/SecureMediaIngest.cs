using First10.Domain.Abstractions;
using First10.Domain.Incidents;
using First10.Infrastructure.Persistence;

namespace First10.Infrastructure.Media;

public sealed record MediaIngestResult(string MediaRef, BlurResult? Blur);

/// <summary>
/// The ONLY legal route from inbound media bytes to persistence (D-009). Every channel
/// adapter — Local cockpit today, WhatsApp/Telegram in M5 — hands raw downloaded bytes
/// here; images pass through the blur gate before the store ever sees them, and each
/// blur operation writes an audit row. An architecture test pins this: no other type
/// may call IMediaStore.SaveAsync.
/// </summary>
public sealed class SecureMediaIngest(IMediaStore store, IFaceBlurrer blurrer, First10DbContext db)
{
    public async Task<MediaIngestResult> IngestAsync(Stream content, string contentType, CancellationToken ct)
    {
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            // Voice notes and other non-image media carry no faces; they pass straight
            // through. (Voice IS personal data — retention and access logging cover it.)
            var passthroughRef = await store.SaveAsync(content, contentType, ct);
            return new MediaIngestResult(passthroughRef, null);
        }

        var blur = await blurrer.BlurAsync(content, ct); // throws NotSupportedException for undecodable bytes

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
}
