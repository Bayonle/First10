using First10.Application.Ingest;
using First10.Application.Triage;
using First10.Domain.Abstractions;
using First10.Domain.Channels;
using First10.Domain.Incidents;
using First10.Domain.Triage;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace First10.Tests;

/// <summary>Regressions from the first live-LLM run (22 Jul).</summary>
public class VoiceAndMismatchRegressionTests
{
    private sealed class NullHasher : IPerceptualHasher
    {
        public Task<ulong> HashAsync(Stream image, CancellationToken ct) => Task.FromResult(0UL);
    }

    /// <summary>Simulates STT mangling a vernacular report into spam-looking text.</summary>
    private sealed class SpamTranscriber : ITranscriber
    {
        public Task<string?> TranscribeAsync(Stream audio, string contentType, CancellationToken ct) =>
            Task.FromResult<string?>("win big promo www.nonsense.example rice beans");
    }

    private static First10DbContext NewDb() =>
        new(new DbContextOptionsBuilder<First10DbContext>()
            .UseInMemoryDatabase($"first10-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public async Task Voice_note_with_spam_looking_transcript_still_reaches_review()
    {
        // Live finding: a gibberish voice note classified spam_or_abuse and was silently
        // dropped. STT can mangle a Yoruba report into nonsense — the dispatcher's ear
        // decides, never the classifier. Voice ALWAYS triages as an incident.
        await using var db = NewDb();

        await IngestInboundMessageHandler.Handle(
            new InboundChannelMessage(ChannelKind.Local, "r1", Guid.NewGuid().ToString("N"),
                InboundKind.Voice, null, "voice.m4a", null, DateTimeOffset.UtcNow),
            db, new HeuristicIntentClassifier(), new SpamTranscriber(), new NullMediaStore(), new NullHasher(),
            new TriageOptions(), NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();

        var ticket = Assert.Single(db.Tickets);
        Assert.Equal(Disposition.Review, ticket.Disposition); // in front of a human, not dropped
        Assert.Equal(EvidenceLevel.VoiceOnly, ticket.Evidence);
    }

    private sealed class MismatchExtractor : IIncidentExtractor
    {
        public Task<ExtractionResult> ExtractAsync(ExtractionInput input, CancellationToken ct) =>
            Task.FromResult(new ExtractionResult(
                SeverityTier.High, null, "rta_generic", "claim vs photo mismatch",
                "test-extract", PhotoMatchesNarrative: false));
    }

    [Fact]
    public async Task Photo_mismatch_caps_uncorroborated_ticket_at_review()
    {
        await using var db = NewDb();
        var conversationId = Guid.NewGuid();
        var ticket = new IncidentTicket
        {
            Id = Guid.NewGuid(),
            Status = TicketStatus.Provisional,
            Summary = "x",
            Disposition = Disposition.FastTrack,
            Evidence = EvidenceLevel.Photo,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Tickets.Add(ticket);
        db.TimelineEntries.Add(new TimelineEntry
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            ConversationId = conversationId,
            Direction = TimelineDirection.Inbound,
            Kind = TimelineEntryKind.Image, // the mismatch guard requires a real photo
            MediaRef = "scene.jpg",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        await RunExtractionHandler.Handle(
            new RunExtraction(ticket.Id, conversationId), db,
            new MismatchExtractor(), new NullMediaStore(), new TriageOptions(),
            NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(Disposition.Review, ticket.Disposition);
        Assert.Contains("photo-mismatch", ticket.Flags);
    }
}
