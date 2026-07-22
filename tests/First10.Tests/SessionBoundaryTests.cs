using First10.Application.Ingest;
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

/// <summary>
/// Session boundary (found via console testing): without it, one conversation is one
/// eternal ticket, and a report sent hours later piles onto last week's incident.
/// </summary>
public class SessionBoundaryTests
{
    private sealed class NullMediaStore : IMediaStore
    {
        public Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct) => Task.FromResult("stub.jpg");
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

    private static async Task<OutgoingMessages> Send(
        First10DbContext db, string sender, InboundKind kind,
        string? text = null, GeoPoint? location = null, string? mediaRef = null)
    {
        var result = await IngestInboundMessageHandler.Handle(
            new InboundChannelMessage(ChannelKind.Local, sender, Guid.NewGuid().ToString("N"),
                kind, text, mediaRef, location, DateTimeOffset.UtcNow),
            db, new HeuristicIntentClassifier(), new NullMediaStore(), new NullHasher(),
            new TriageOptions(), NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();
        return result;
    }

    /// <summary>Simulate silence by backdating the conversation's last inbound.</summary>
    private static async Task Backdate(First10DbContext db, int minutes)
    {
        var conversation = db.Conversations.Single();
        conversation.LastInboundAt = DateTimeOffset.UtcNow.AddMinutes(-minutes);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Message_after_long_silence_opens_a_new_incident()
    {
        await using var db = NewDb();

        await Send(db, "r1", InboundKind.Text, "accident for road");
        await Backdate(db, 20); // > 15 min default window

        await Send(db, "r1", InboundKind.Image, mediaRef: "scene.jpg");

        Assert.Equal(2, db.Tickets.Count()); // NOT enriched onto the stale ticket
        var newest = db.Tickets.OrderBy(t => t.CreatedAt).Last();
        Assert.Equal(EvidenceLevel.Photo, newest.Evidence);
        Assert.Equal(TicketStatus.Provisional, newest.Status);
    }

    [Fact]
    public async Task Unanswered_challenge_expires_but_stays_visible()
    {
        await using var db = NewDb();

        await Send(db, "r1", InboundKind.Text, "accident for road"); // challenge sent
        await Backdate(db, 20);
        await Send(db, "r1", InboundKind.Text, "another accident here");

        var stale = db.Tickets.OrderBy(t => t.CreatedAt).First();
        Assert.Equal(TicketStatus.ExpiredUnverified, stale.Status); // visible, human makes the kill call
        Assert.Contains(db.TimelineEntries,
            e => e.TicketId == stale.Id && e.Direction == TimelineDirection.System
                && e.Text!.Contains("challenge was never answered"));
    }

    [Fact]
    public async Task Partially_evidenced_ticket_stays_pending_when_session_closes()
    {
        await using var db = NewDb();

        await Send(db, "r1", InboundKind.Text, "accident for road");
        await Send(db, "r1", InboundKind.LocationPin, location: new GeoPoint(6.806, 3.437)); // pin answered
        await Backdate(db, 20);
        await Send(db, "r1", InboundKind.Text, "there's another crash oh, accident again");

        var first = db.Tickets.OrderBy(t => t.CreatedAt).First();
        // Text + resolved location is dispatchable on a corridor — the incident must
        // NOT expire just because the reporter went quiet.
        Assert.Equal(TicketStatus.Provisional, first.Status);
        Assert.NotNull(first.LocationResolvedAt);
        Assert.Equal(2, db.Tickets.Count());
    }

    [Fact]
    public async Task Messages_within_the_window_still_enrich_the_open_ticket()
    {
        await using var db = NewDb();

        await Send(db, "r1", InboundKind.Text, "accident for road");
        await Backdate(db, 10); // inside the 15-min window
        await Send(db, "r1", InboundKind.Image, mediaRef: "scene.jpg");

        var ticket = Assert.Single(db.Tickets);
        Assert.Equal(EvidenceLevel.Photo, ticket.Evidence); // enriched, not split
    }
}
