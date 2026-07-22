namespace First10.Domain.Triage;

public enum Disposition
{
    /// <summary>No ticket warranted (greeting/question) — canned reply only.</summary>
    None = 0,
    Drop = 1,
    /// <summary>Review queue + elicitation challenge sent (text-only reports).</summary>
    Challenge = 2,
    Review = 3,
    FastTrack = 4,
    /// <summary>Requires multi-reporter corroboration — reachable from M2 (dedup/merge).</summary>
    AutoVerify = 5,
}

public enum EvidenceLevel
{
    None = 0,
    TextOnly = 1,
    VoiceOnly = 2,
    Photo = 3,
    /// <summary>Photo plus voice and/or location pin.</summary>
    PhotoPlus = 4,
}

public enum TrustLevel
{
    Blocked = 0,
    Low = 1,
    Neutral = 2,
    /// <summary>Trained corridor volunteers (seeded at onboarding).</summary>
    High = 3,
}

public sealed record TriageInput(
    MessageIntent Intent,
    EvidenceLevel Evidence,
    TrustLevel Trust,
    bool RateLimited,
    bool FloodActive,
    bool ReusedImage,
    bool OutsideCorridor);

public sealed record TriageDecision(
    Disposition Disposition,
    bool SendChallenge,
    IReadOnlyList<string> Flags);

/// <summary>
/// Stage 2 (D-008): pure decision function — evidence gates disposition, trust and
/// flood cap it, flags never silently drop. The asymmetry rule is structural here:
/// nothing a classifier says can push a plausible report below Review; only hard
/// signals (blocked reporter, rate limit, spam intent with no evidence) can Drop.
/// </summary>
public static class DispositionEngine
{
    public static TriageDecision Decide(TriageInput input)
    {
        var flags = new List<string>();

        if (input.RateLimited)
        {
            return new TriageDecision(Disposition.Drop, false, ["rate-limited"]);
        }

        if (input.Trust == TrustLevel.Blocked)
        {
            return new TriageDecision(Disposition.Drop, false, ["blocked-reporter"]);
        }

        if (input.Intent is MessageIntent.GreetingOrTest or MessageIntent.Question)
        {
            return new TriageDecision(Disposition.None, false, []);
        }

        if (input.Intent == MessageIntent.SpamOrAbuse && input.Evidence < EvidenceLevel.Photo)
        {
            return new TriageDecision(Disposition.Drop, false, ["spam-intent"]);
        }

        // Base disposition from evidence — the ceiling table in D-008.
        var disposition = input.Evidence switch
        {
            EvidenceLevel.PhotoPlus or EvidenceLevel.Photo => Disposition.FastTrack,
            EvidenceLevel.VoiceOnly => Disposition.Review,
            _ => Disposition.Challenge, // text-only: review queue + elicitation
        };

        // Text-only never auto-dispatches and never silently drops — challenge converts
        // real reporters into full-evidence reports and starves spam (D-008).
        var sendChallenge = disposition == Disposition.Challenge;

        if (input.Intent == MessageIntent.SpamOrAbuse)
        {
            // Photo evidence outranks a text spam signal, but a human must look.
            disposition = Cap(disposition, Disposition.Review);
            flags.Add("spam-intent-with-evidence");
        }

        if (input.ReusedImage)
        {
            disposition = Cap(disposition, Disposition.Review);
            flags.Add("reused-image");
        }

        if (input.Trust == TrustLevel.Low)
        {
            disposition = Cap(disposition, Disposition.Review);
            flags.Add("low-trust-reporter");
        }

        if (input.FloodActive)
        {
            disposition = Cap(disposition, Disposition.Review); // R11
            flags.Add("flood-active");
        }

        if (input.OutsideCorridor)
        {
            flags.Add("outside-corridor"); // flag, never drop — could be a mislocated real reporter
        }

        return new TriageDecision(disposition, sendChallenge, flags);
    }

    private static Disposition Cap(Disposition current, Disposition ceiling) =>
        current > ceiling ? ceiling : current;
}
