using First10.Domain.Abstractions;

namespace First10.Domain.Incidents;

/// <summary>
/// One row per blur operation (paper §1.4 commitment): what the gate saw, what it did,
/// and how long it took. The receipt-to-blur ≤ 1s target (§7.1) is measured off DurationMs.
/// </summary>
public class BlurAuditRecord
{
    public Guid Id { get; set; }
    public string MediaRef { get; set; } = default!;
    public int FacesDetected { get; set; }
    public int LowConfidenceRegions { get; set; }
    public double? MinConfidence { get; set; }
    public BlurFallback Fallback { get; set; }
    public long DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
