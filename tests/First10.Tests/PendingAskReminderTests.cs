using First10.Application.Ingest;
using First10.Application.Outbound;
using First10.Application.Triage;
using First10.Domain.Abstractions;
using First10.Domain.Channels;
using First10.Domain.Triage;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wolverine;

namespace First10.Tests;

/// <summary>
/// "Never silent, never nagging" (found via cockpit testing): a reporter asking
/// "what's happening?" mid-flow must get the pending ask restated, not silence —
/// but rapid-fire messages must not turn the system into a nag bot.
/// </summary>
public class PendingAskReminderTests
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
        First10DbContext db, InboundKind kind,
        string? text = null, GeoPoint? location = null, string? mediaRef = null)
    {
        var result = await IngestInboundMessageHandler.Handle(
            new InboundChannelMessage(ChannelKind.Local, "r1", Guid.NewGuid().ToString("N"),
                kind, text, mediaRef, location, DateTimeOffset.UtcNow),
            db, new HeuristicIntentClassifier(), new TestNullTranscriber(), new NullMediaStore(), new NullHasher(),
            new TriageOptions(), NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();
        return result;
    }

    /// <summary>Age the ticket's outbound history so the reminder throttle opens.</summary>
    private static async Task AgeOutboundHistory(First10DbContext db, int seconds)
    {
        var ticket = db.Tickets.Single();
        var shift = TimeSpan.FromSeconds(seconds);
        ticket.CreatedAt -= shift;
        if (ticket.ChallengeSentAt is { } c) ticket.ChallengeSentAt = c - shift;
        if (ticket.LocationRequestSentAt is { } l) ticket.LocationRequestSentAt = l - shift;
        if (ticket.AckSentAt is { } a) ticket.AckSentAt = a - shift;
        if (ticket.LocationResolvedAt is { } r) ticket.LocationResolvedAt = r - shift;
        if (ticket.LastReminderSentAt is { } m) ticket.LastReminderSentAt = m - shift;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Question_while_pin_pending_restates_the_pin_request()
    {
        await using var db = NewDb();
        await Send(db, InboundKind.Text, "an accident happened on the road. please help");
        await Send(db, InboundKind.Image, mediaRef: "scene.jpg"); // → pin request sent
        await AgeOutboundHistory(db, 60); // past the 30s text throttle

        var outgoing = await Send(db, InboundKind.Text, "what's happening?");

        Assert.Contains(outgoing, m => m is SendOutboundMessage { Kind: OutboundKind.LocationPinRequest });
    }

    [Fact]
    public async Task Immediate_duplicate_photo_stays_quiet()
    {
        await using var db = NewDb();
        await Send(db, InboundKind.Text, "an accident happened on the road. please help");
        await Send(db, InboundKind.Image, mediaRef: "scene.jpg"); // pin request just sent

        var outgoing = await Send(db, InboundKind.Image, mediaRef: "scene2.jpg"); // 0s later

        Assert.DoesNotContain(outgoing, m => m is SendOutboundMessage); // 120s media throttle
    }

    [Fact]
    public async Task Question_on_completed_report_gets_status_reply()
    {
        await using var db = NewDb();
        await Send(db, InboundKind.Text, "an accident happened on the road. please help");
        await Send(db, InboundKind.LocationPin, location: new GeoPoint(6.806, 3.437));
        await Send(db, InboundKind.Image, mediaRef: "scene.jpg"); // → ReportAck, complete
        await AgeOutboundHistory(db, 60);

        var outgoing = await Send(db, InboundKind.Text, "what's happening?");

        Assert.Contains(outgoing, m => m is SendOutboundMessage { Kind: OutboundKind.StatusUnderReview });
    }

    [Fact]
    public async Task Reminders_throttle_repeated_questions()
    {
        await using var db = NewDb();
        await Send(db, InboundKind.Text, "an accident happened on the road. please help");
        await Send(db, InboundKind.Image, mediaRef: "scene.jpg");
        await AgeOutboundHistory(db, 60);

        var first = await Send(db, InboundKind.Text, "what's happening?");
        var second = await Send(db, InboundKind.Text, "hello??"); // seconds later

        Assert.Contains(first, m => m is SendOutboundMessage);
        Assert.DoesNotContain(second, m => m is SendOutboundMessage); // within 30s of the reminder
    }
}
