namespace First10.Domain.Triage;

public sealed record CorridorLandmark(
    string Key, string Name, string[] Aliases, double Lat, double Lng, double RadiusKm);

/// <summary>
/// Named places along Berger–Mowe that reporters actually say ("accident for Kara
/// bridge") — the corridor's natural addressing system, and how FRSC dispatches anyway.
/// Extraction SELECTS a key from this closed list (never invents coordinates — the
/// same safety pattern as clinical templates); the ticket then carries an APPROXIMATE
/// location distinct from a pin (LocationSource.LandmarkInferred).
///
/// Coordinates are PROVISIONAL: interpolated along the corridor centerline from public
/// map data. TODO(M5): verify with FRSC Ogun in the same session as the geofence
/// waypoints — one meeting blesses both.
///
/// Alias discipline: aliases must be words that are unambiguous in crash-report prose.
/// ("punch" alone would match pidgin violence — "dem punch am"; "camp" is generic.)
/// </summary>
public static class CorridorLandmarks
{
    public static readonly CorridorLandmark[] All =
    [
        new("berger", "Berger interchange", ["berger"], 6.6435, 3.3655, 1.0),
        new("kara", "Kara bridge", ["kara", "carra", "cattle market"], 6.6650, 3.3830, 1.0),
        new("longbridge", "Long bridge", ["long bridge", "longbridge"], 6.6760, 3.3930, 1.5),
        new("opic", "OPIC (Isheri North)", ["opic", "isheri"], 6.6900, 3.4050, 1.0),
        new("arepo", "Arepo", ["arepo"], 6.7030, 3.4110, 1.0),
        new("warewa", "Warewa", ["warewa"], 6.7140, 3.4170, 1.0),
        new("magboro", "Magboro (Punch flyover)", ["magboro", "punch flyover"], 6.7240, 3.4220, 1.0),
        new("ibafo", "Ibafo", ["ibafo"], 6.7430, 3.4300, 1.0),
        new("asese", "Asese", ["asese"], 6.7660, 3.4340, 1.2),
        new("redemption", "Redemption Camp", ["redemption", "rccg"], 6.7860, 3.4360, 1.5),
        new("mowe", "Mowe / Ofada junction", ["mowe", "ofada"], 6.8060, 3.4370, 1.0),
    ];

    public static CorridorLandmark? ByKey(string? key) =>
        key is null ? null : All.FirstOrDefault(l => l.Key == key.Trim().ToLowerInvariant());

    /// <summary>Alias containment match on normalized text — the heuristic extractor's path.</summary>
    public static CorridorLandmark? Match(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var normalized = Normalize(text);
        return All.FirstOrDefault(l => l.Aliases.Any(a => normalized.Contains(Normalize(a))));
    }

    private static string Normalize(string s) =>
        new(s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());
}
