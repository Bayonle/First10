using First10.Application.Ingest;
using First10.Application.Outbound;
using First10.Application.Triage;
using First10.Domain.Abstractions;
using First10.Domain.Channels;
using First10.Domain.Incidents;
using First10.Domain.Triage;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wolverine;

namespace First10.Tests;

public class IngestInboundMessageHandlerTests
{
    private sealed class NullMediaStore : IMediaStore
    {
        public Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct) =>
            Task.FromResult("stub.jpg");
        public Task<Stream?> OpenReadAsync(string mediaRef, CancellationToken ct) =>
            Task.FromResult<Stream?>(null); // pHash skipped
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

    private static Task<OutgoingMessages> Handle(First10DbContext db, InboundChannelMessage msg, TriageOptions? options = null) =>
        IngestInboundMessageHandler.Handle(
            msg, db, new HeuristicIntentClassifier(), new NullMediaStore(), new NullHasher(),
            options ?? new TriageOptions(), NullLogger.Instance, CancellationToken.None);

    private static InboundChannelMessage Message(
        string sender = "persona-1",
        string? externalId = null,
        string text = "Accident dey happen for Mowe o! Two okada down",
        InboundKind kind = InboundKind.Text,
        string? mediaRef = null,
        GeoPoint? location = null) =>
        new(ChannelKind.Local, sender, externalId ?? Guid.NewGuid().ToString("N"),
            kind, kind == InboundKind.Text ? text : null, mediaRef, location, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Incident_text_opens_provisional_ticket_with_triage()
    {
        await using var db = NewDb();

        var outgoing = await Handle(db, Message());
        await db.SaveChangesAsync();

        var ticket = Assert.Single(db.Tickets);
        Assert.Equal(TicketStatus.Provisional, ticket.Status);           // D-007
        Assert.Equal(Disposition.Challenge, ticket.Disposition);         // text-only → review + challenge
        Assert.Equal(EvidenceLevel.TextOnly, ticket.Evidence);
        Assert.Equal("pidgin", ticket.Language);
        Assert.NotNull(ticket.ChallengeSentAt);
        Assert.Contains(outgoing, m => m is SendOutboundMessage { Kind: OutboundKind.ElicitationChallenge });
        Assert.Contains(outgoing, m => m is TicketUpserted);
    }

    [Fact]
    public async Task Greeting_gets_canned_reply_and_no_ticket()
    {
        await using var db = NewDb();

        var outgoing = await Handle(db, Message(text: "Good morning"));
        await db.SaveChangesAsync();

        Assert.Empty(db.Tickets);
        Assert.Contains(outgoing, m => m is SendOutboundMessage { Kind: OutboundKind.CannedReply });
        var entry = Assert.Single(db.TimelineEntries);
        Assert.Null(entry.TicketId); // conversation-scoped, no incident
    }

    [Fact]
    public async Task Photo_is_evidence_first_and_fast_tracks_without_classifier()
    {
        await using var db = NewDb();

        await Handle(db, Message(kind: InboundKind.Image, mediaRef: "crash.jpg"));
        await db.SaveChangesAsync();

        var ticket = Assert.Single(db.Tickets);
        Assert.Equal(Disposition.FastTrack, ticket.Disposition);
        Assert.Equal(EvidenceLevel.Photo, ticket.Evidence);
        Assert.Equal("evidence-first", ticket.ClassifierVersion);
        Assert.Null(ticket.ChallengeSentAt);
    }

    [Fact]
    public async Task Challenge_answered_with_photo_raises_disposition()
    {
        await using var db = NewDb();

        await Handle(db, Message(text: "accident near Ibafo, please come"));
        await db.SaveChangesAsync();
        await Handle(db, Message(kind: InboundKind.Image, mediaRef: "scene.jpg"));
        await db.SaveChangesAsync();

        var ticket = Assert.Single(db.Tickets);
        Assert.Equal(Disposition.FastTrack, ticket.Disposition); // Challenge → FastTrack on photo
        Assert.Equal(EvidenceLevel.Photo, ticket.Evidence);
        // System note recorded the transition for the console (D-013)
        Assert.Contains(db.TimelineEntries, e =>
            e.Direction == TimelineDirection.System && e.Text!.Contains("Evidence received"));
    }

    [Fact]
    public async Task Rate_limited_conversation_gets_dropped()
    {
        await using var db = NewDb();
        var options = new TriageOptions { MaxNewIncidentsPerWindow = 1 };

        // First incident opens; close it so the next message is a NEW incident attempt.
        await Handle(db, Message(text: "accident at Kara bridge"), options);
        await db.SaveChangesAsync();
        var first = db.Tickets.Single();
        first.Status = TicketStatus.Closed;
        db.Conversations.Single().ActiveTicketId = null;
        await db.SaveChangesAsync();

        var outgoing = await Handle(db, Message(text: "another accident here again"), options);
        await db.SaveChangesAsync();

        Assert.Single(db.Tickets); // no second ticket
        Assert.Empty(outgoing);
    }

    [Fact]
    public async Task Flood_caps_new_tickets_at_review()
    {
        await using var db = NewDb();
        var options = new TriageOptions { FloodDistinctConversations = 2, FloodWindowMinutes = 10 };

        await Handle(db, Message(sender: "p1", kind: InboundKind.Image, mediaRef: "a.jpg"), options);
        await db.SaveChangesAsync();
        await Handle(db, Message(sender: "p2", kind: InboundKind.Image, mediaRef: "b.jpg"), options);
        await db.SaveChangesAsync();
        await Handle(db, Message(sender: "p3", kind: InboundKind.Image, mediaRef: "c.jpg"), options);
        await db.SaveChangesAsync();

        var flooded = db.Tickets.OrderBy(t => t.CreatedAt).Last();
        Assert.Equal(Disposition.Review, flooded.Disposition); // capped despite photo evidence (R11)
        Assert.Contains("flood-active", flooded.Flags);
    }

    [Fact]
    public async Task Blocked_reporter_is_dropped_silently()
    {
        await using var db = NewDb();
        db.ReporterReputations.Add(new First10.Domain.Conversations.ReporterReputation
        {
            Id = Guid.NewGuid(),
            Channel = ChannelKind.Local,
            ExternalUserId = "spammer",
            Trust = TrustLevel.Blocked,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var outgoing = await Handle(db, Message(sender: "spammer"));
        await db.SaveChangesAsync();

        Assert.Empty(db.Tickets);
        Assert.Empty(db.TimelineEntries);
        Assert.Empty(outgoing);
    }

    [Fact]
    public async Task Off_corridor_pin_is_flagged_never_dropped()
    {
        await using var db = NewDb();

        await Handle(db, Message(text: "bad crash two cars here"));
        await db.SaveChangesAsync();
        await Handle(db, Message(kind: InboundKind.LocationPin, location: new GeoPoint(9.0765, 7.3986))); // Abuja
        await db.SaveChangesAsync();

        var ticket = Assert.Single(db.Tickets);
        Assert.Contains("outside-corridor", ticket.Flags);
        // Challenge == review queue + elicitation; the point is it's in front of a human, not dropped.
        Assert.True(ticket.Disposition is Disposition.Challenge or Disposition.Review or Disposition.FastTrack);
    }

    [Fact]
    public async Task Redelivered_message_is_dropped()
    {
        await using var db = NewDb();
        const string redeliveredId = "wamid-123";

        await Handle(db, Message(externalId: redeliveredId));
        await db.SaveChangesAsync();
        var second = await Handle(db, Message(externalId: redeliveredId));
        await db.SaveChangesAsync();

        Assert.Empty(second);
        Assert.Equal(1, db.TimelineEntries.Count(e => e.Direction == TimelineDirection.Inbound));
    }
}
