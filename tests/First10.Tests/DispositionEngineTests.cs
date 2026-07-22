using First10.Domain.Triage;

namespace First10.Tests;

/// <summary>D-008 ceiling table + caps. If these fail, the funnel's safety story is broken.</summary>
public class DispositionEngineTests
{
    private static TriageInput Input(
        MessageIntent intent = MessageIntent.NewIncident,
        EvidenceLevel evidence = EvidenceLevel.TextOnly,
        TrustLevel trust = TrustLevel.Neutral,
        bool rateLimited = false,
        bool flood = false,
        bool reusedImage = false,
        bool outsideCorridor = false) =>
        new(intent, evidence, trust, rateLimited, flood, reusedImage, outsideCorridor);

    [Theory]
    [InlineData(EvidenceLevel.PhotoPlus, Disposition.FastTrack)]
    [InlineData(EvidenceLevel.Photo, Disposition.FastTrack)]
    [InlineData(EvidenceLevel.VoiceOnly, Disposition.Review)]
    [InlineData(EvidenceLevel.TextOnly, Disposition.Challenge)]
    public void Evidence_sets_the_ceiling(EvidenceLevel evidence, Disposition expected)
    {
        var decision = DispositionEngine.Decide(Input(evidence: evidence));
        Assert.Equal(expected, decision.Disposition);
    }

    [Fact]
    public void Text_only_gets_challenge_never_drop()
    {
        var decision = DispositionEngine.Decide(Input(evidence: EvidenceLevel.TextOnly));
        Assert.True(decision.SendChallenge);
        Assert.NotEqual(Disposition.Drop, decision.Disposition);
    }

    [Fact]
    public void Spam_without_evidence_drops_but_spam_with_photo_reaches_review()
    {
        Assert.Equal(Disposition.Drop,
            DispositionEngine.Decide(Input(intent: MessageIntent.SpamOrAbuse)).Disposition);

        var withPhoto = DispositionEngine.Decide(
            Input(intent: MessageIntent.SpamOrAbuse, evidence: EvidenceLevel.Photo));
        Assert.Equal(Disposition.Review, withPhoto.Disposition);
        Assert.Contains("spam-intent-with-evidence", withPhoto.Flags);
    }

    [Theory]
    [InlineData(MessageIntent.GreetingOrTest)]
    [InlineData(MessageIntent.Question)]
    public void Non_incidents_produce_no_ticket(MessageIntent intent)
    {
        Assert.Equal(Disposition.None, DispositionEngine.Decide(Input(intent: intent)).Disposition);
    }

    [Fact]
    public void Flood_caps_fast_track_at_review()
    {
        var decision = DispositionEngine.Decide(Input(evidence: EvidenceLevel.Photo, flood: true));
        Assert.Equal(Disposition.Review, decision.Disposition);
        Assert.Contains("flood-active", decision.Flags);
    }

    [Fact]
    public void Reused_image_caps_at_review()
    {
        var decision = DispositionEngine.Decide(Input(evidence: EvidenceLevel.PhotoPlus, reusedImage: true));
        Assert.Equal(Disposition.Review, decision.Disposition);
        Assert.Contains("reused-image", decision.Flags);
    }

    [Fact]
    public void Outside_corridor_flags_without_capping()
    {
        var decision = DispositionEngine.Decide(Input(evidence: EvidenceLevel.Photo, outsideCorridor: true));
        Assert.Equal(Disposition.FastTrack, decision.Disposition);
        Assert.Contains("outside-corridor", decision.Flags);
    }

    [Fact]
    public void Rate_limit_and_blocked_reporter_drop()
    {
        Assert.Equal(Disposition.Drop, DispositionEngine.Decide(Input(rateLimited: true)).Disposition);
        Assert.Equal(Disposition.Drop, DispositionEngine.Decide(Input(trust: TrustLevel.Blocked)).Disposition);
    }
}
