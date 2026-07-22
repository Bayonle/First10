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

/// <summary>Regressions from the wild-human-behavior sweep (22 Jul): people fat-finger
/// pins, retract reports, and send fragments — the system must absorb all of it.</summary>
public class WildHumanBehaviorTests
{
    private sealed class NullMediaStore : IMediaStore
    {
        public Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct) => Task.FromResult("x");
        public Task<Stream?> OpenReadAsync(string mediaRef, CancellationToken ct) => Task.FromResult<Stream?>(null);
        public string GetContentType(string mediaRef) => "image/jpeg";
    }

    private sealed class NullHasher : IPerceptualHasher
    {
        public Task<ulong> HashAsync(Stream image, CancellationToken ct) => Task.FromResult(0UL);
    }

    private static First10DbContext NewDb() =>
        new(new DbContextOptionsBuilder<First10DbContext>()
            .UseInMemoryDatabase($"first10-{Guid.NewGuid()}")
            .Options);

    private static async Task Send(First10DbContext db, InboundKind kind, string? text = null, GeoPoint? location = null)
    {
        await IngestInboundMessageHandler.Handle(
            new InboundChannelMessage(ChannelKind.Local, "r1", Guid.NewGuid().ToString("N"),
                kind, text, null, location, DateTimeOffset.UtcNow),
            db, new HeuristicIntentClassifier(), new TestNullTranscriber(), new NullMediaStore(), new NullHasher(),
            new TriageOptions(), NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Corrected_pin_replaces_the_wrong_location_and_clears_the_corridor_flag()
    {
        await using var db = NewDb();

        await Send(db, InboundKind.Text, "accident for kara bridge");
        await Send(db, InboundKind.LocationPin, location: new GeoPoint(9.0765, 7.3986)); // fat-finger: Abuja
        var ticket = db.Tickets.Single();
        Assert.Contains("outside-corridor", ticket.Flags);

        await Send(db, InboundKind.Text, "sorry wrong pin, na this one");
        await Send(db, InboundKind.LocationPin, location: new GeoPoint(6.665, 3.383)); // correction: Kara

        Assert.Equal(6.665, ticket.LocationLat!.Value, 3);
        Assert.True(ticket.Flags is null || !ticket.Flags.Contains("outside-corridor"));
        Assert.Contains(db.TimelineEntries, e =>
            e.Direction == TimelineDirection.System && e.Text!.Contains("Location updated by reporter"));
    }

    [Fact]
    public async Task Fat_finger_INTO_the_corridor_flags_the_other_way()
    {
        await using var db = NewDb();

        await Send(db, InboundKind.Text, "accident happened here now");
        await Send(db, InboundKind.LocationPin, location: new GeoPoint(6.665, 3.383)); // on-corridor
        await Send(db, InboundKind.LocationPin, location: new GeoPoint(9.0765, 7.3986)); // "correction" to Abuja

        var ticket = db.Tickets.Single();
        Assert.Contains("outside-corridor", ticket.Flags); // flag follows the current pin
    }

    private sealed class NoPhotoMismatchExtractor : IIncidentExtractor
    {
        public Task<ExtractionResult> ExtractAsync(ExtractionInput input, CancellationToken ct) =>
            Task.FromResult(new ExtractionResult(
                SeverityTier.Medium, null, "rta_generic", "summary", "test",
                PhotoMatchesNarrative: false)); // model claims mismatch with NO photo
    }

    [Fact]
    public async Task Photo_mismatch_is_ignored_when_there_is_no_photo()
    {
        // Live finding: models return photo_matches_narrative=false on text-only input;
        // every text ticket got a bogus photo-mismatch flag.
        await using var db = NewDb();
        var conversationId = Guid.NewGuid();
        var ticket = new IncidentTicket
        {
            Id = Guid.NewGuid(),
            Status = TicketStatus.Provisional,
            Summary = "x",
            Disposition = Disposition.FastTrack,
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
            Kind = TimelineEntryKind.Text, // no image anywhere
            Text = "accident",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        await RunExtractionHandler.Handle(
            new RunExtraction(ticket.Id, conversationId), db,
            new NoPhotoMismatchExtractor(), new NullMediaStore(), new TriageOptions(),
            NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.True(ticket.Flags is null || !ticket.Flags.Contains("photo-mismatch"));
        Assert.Equal(Disposition.FastTrack, ticket.Disposition); // not capped
    }
}
