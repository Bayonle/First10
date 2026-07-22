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

/// <summary>
/// Paper §1.4: two independent reporters within 200m/5min auto-verify a shared
/// incident. The only disposition that can reach AUTO_VERIFY (D-008).
/// </summary>
public class CorroborationTests
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
            db, new HeuristicIntentClassifier(), new TestNullTranscriber(), new NullMediaStore(), new NullHasher(),
            new TriageOptions(), NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();
        return result;
    }

    private static readonly GeoPoint KaraBridge = new(6.6650, 3.3830);
    private static readonly GeoPoint KaraBridge100mAway = new(6.6659, 3.3830); // ~100m north

    [Fact]
    public async Task Second_reporter_pin_merges_into_first_incident_and_auto_verifies()
    {
        await using var db = NewDb();

        // Reporter A: text + pin
        await Send(db, "rep-a", InboundKind.Text, "trailer don fall for Kara bridge");
        await Send(db, "rep-a", InboundKind.LocationPin, location: KaraBridge);

        // Reporter B: independent report, pin ~100m away, minutes later
        await Send(db, "rep-b", InboundKind.Text, "accident at Kara bridge, trailer overturned");
        var outgoing = await Send(db, "rep-b", InboundKind.LocationPin, location: KaraBridge100mAway);

        var tickets = db.Tickets.OrderBy(t => t.CreatedAt).ToList();
        Assert.Equal(2, tickets.Count);
        var survivor = tickets[0];
        var merged = tickets[1];

        Assert.Equal(TicketStatus.Merged, merged.Status);
        Assert.Equal(Disposition.AutoVerify, survivor.Disposition);
        Assert.Equal(2, survivor.ReporterCount);
        Assert.Contains("corroborated", survivor.Flags);
        // Promotion rule: corroboration + location ⇒ Promoted (D-007)
        Assert.Equal(TicketStatus.Promoted, survivor.Status);

        // Relay: B's entries re-pointed onto the survivor, per-reporter identity kept
        var survivorConversations = db.TimelineEntries
            .Where(e => e.TicketId == survivor.Id && e.Direction == TimelineDirection.Inbound)
            .Select(e => e.ConversationId).Distinct().Count();
        Assert.Equal(2, survivorConversations);

        // B's session now feeds the shared incident; B was acknowledged
        Assert.Equal(survivor.Id, db.Conversations.Single(c => c.ExternalUserId == "rep-b").ActiveTicketId);
        Assert.Contains(outgoing, m => m is SendOutboundMessage { Kind: OutboundKind.ReportAck });
    }

    [Fact]
    public async Task Pin_bearing_first_message_attaches_directly_to_existing_incident()
    {
        await using var db = NewDb();

        await Send(db, "rep-a", InboundKind.Text, "accident for Kara bridge o");
        await Send(db, "rep-a", InboundKind.LocationPin, location: KaraBridge);

        // Reporter B's FIRST message is already a pin near the incident
        await Send(db, "rep-b", InboundKind.LocationPin, location: KaraBridge100mAway);

        var ticket = Assert.Single(db.Tickets, t => t.Status != TicketStatus.Merged);
        Assert.Equal(2, ticket.ReporterCount);
        Assert.Equal(Disposition.AutoVerify, ticket.Disposition);
        Assert.Single(db.Tickets); // no second ticket was ever created
    }

    [Fact]
    public async Task Far_away_pins_do_not_merge()
    {
        await using var db = NewDb();

        await Send(db, "rep-a", InboundKind.Text, "accident for Kara bridge");
        await Send(db, "rep-a", InboundKind.LocationPin, location: KaraBridge);

        await Send(db, "rep-b", InboundKind.Text, "accident near Ibafo");
        await Send(db, "rep-b", InboundKind.LocationPin, location: new GeoPoint(6.7430, 3.4300)); // ~10km away

        Assert.Equal(2, db.Tickets.Count(t => t.Status != TicketStatus.Merged)); // separate incidents
        Assert.All(db.Tickets, t => Assert.Equal(1, t.ReporterCount));
    }

    [Fact]
    public async Task Same_reporter_cannot_corroborate_their_own_incident()
    {
        await using var db = NewDb();

        await Send(db, "rep-a", InboundKind.Text, "accident for Kara bridge");
        await Send(db, "rep-a", InboundKind.LocationPin, location: KaraBridge);
        // Session ends; same reporter reports again nearby
        db.Conversations.Single().LastInboundAt = DateTimeOffset.UtcNow.AddMinutes(-20);
        await db.SaveChangesAsync();
        await Send(db, "rep-a", InboundKind.LocationPin, location: KaraBridge100mAway);

        // Independence requirement: no AUTO_VERIFY from one phone number
        Assert.DoesNotContain(db.Tickets, t => t.Disposition == Disposition.AutoVerify);
    }
}
