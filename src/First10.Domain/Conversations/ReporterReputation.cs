using First10.Domain.Channels;
using First10.Domain.Triage;

namespace First10.Domain.Conversations;

/// <summary>
/// Sticky per-reporter trust (D-008). Trained volunteers are seeded High at onboarding;
/// dispatcher-confirmed false reports drop a reporter to Low (wired in M3 outcome marking).
/// Absent row = Neutral.
/// </summary>
public class ReporterReputation
{
    public Guid Id { get; set; }
    public ChannelKind Channel { get; set; }
    public string ExternalUserId { get; set; } = default!;
    public TrustLevel Trust { get; set; } = TrustLevel.Neutral;
    public string? Note { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
