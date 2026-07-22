namespace First10.Domain.Incidents;

/// <summary>
/// Clinically pre-approved safety micro-instruction (paper §1.4). The AI SELECTS a
/// template by key; it never generates clinical text (D-011/D-014). Templates without
/// clinical approval are never sent in pilot configuration — the send path checks
/// ApprovedAt unless the dev-only TriageOptions.AllowUnapprovedTemplates is set.
/// </summary>
public class MicroInstructionTemplate
{
    public Guid Id { get; set; }

    /// <summary>Stable selection key, e.g. "rta_generic", "rta_fire", "rta_okada".</summary>
    public string Key { get; set; } = default!;

    /// <summary>"english" | "pidgin" | "yoruba".</summary>
    public string Language { get; set; } = default!;

    public string Text { get; set; } = default!;

    /// <summary>Pre-recorded human voice note (D-011); null until recorded.</summary>
    public string? AudioMediaRef { get; set; }

    public int Version { get; set; } = 1;

    /// <summary>Clinical sign-off (G3 gate). Null = NOT sendable in pilot config.</summary>
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
}
