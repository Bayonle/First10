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
/// The reporter must never wonder whether their message landed (paper §1.2.5).
/// Found via cockpit testing: pin after challenge produced total silence.
/// </summary>
public class ReporterFeedbackTests
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

    private static readonly GeoPoint Mowe = new(6.806, 3.437);

    [Fact]
    public async Task Pin_after_challenge_gets_acknowledged_and_resolves_location()
    {
        await using var db = NewDb();

        await Send(db, "r1", InboundKind.Text, "accident for road");
        var outgoing = await Send(db, "r1", InboundKind.LocationPin, location: Mowe);

        var ticket = Assert.Single(db.Tickets);
        Assert.NotNull(ticket.LocationResolvedAt);
        Assert.Contains(outgoing, m => m is SendOutboundMessage { Kind: OutboundKind.PinReceivedAck });
    }

    [Fact]
    public async Task Photo_after_pin_completes_the_report_with_single_ack()
    {
        await using var db = NewDb();

        await Send(db, "r1", InboundKind.Text, "accident for road");
        await Send(db, "r1", InboundKind.LocationPin, location: Mowe);
        var outgoing = await Send(db, "r1", InboundKind.Image, mediaRef: "scene.jpg");

        var ticket = Assert.Single(db.Tickets);
        Assert.NotNull(ticket.AckSentAt);
        Assert.Contains(outgoing, m => m is SendOutboundMessage { Kind: OutboundKind.ReportAck });

        // A second photo must not re-ack.
        var again = await Send(db, "r1", InboundKind.Image, mediaRef: "scene2.jpg");
        Assert.DoesNotContain(again, m => m is SendOutboundMessage);
    }

    [Fact]
    public async Task Photo_first_report_gets_pin_request()
    {
        await using var db = NewDb();

        var outgoing = await Send(db, "r1", InboundKind.Image, mediaRef: "scene.jpg");

        var ticket = Assert.Single(db.Tickets);
        Assert.NotNull(ticket.LocationRequestSentAt);
        Assert.Contains(outgoing, m => m is SendOutboundMessage { Kind: OutboundKind.LocationPinRequest });
    }

    [Fact]
    public async Task Pin_after_photo_completes_the_report()
    {
        await using var db = NewDb();

        await Send(db, "r1", InboundKind.Image, mediaRef: "scene.jpg");
        var outgoing = await Send(db, "r1", InboundKind.LocationPin, location: Mowe);

        Assert.Contains(outgoing, m => m is SendOutboundMessage { Kind: OutboundKind.ReportAck });
    }

    [Fact]
    public async Task Repeated_greetings_get_one_canned_reply_not_three()
    {
        await using var db = NewDb();

        var first = await Send(db, "r1", InboundKind.Text, "Hello");
        var second = await Send(db, "r1", InboundKind.Text, "hi");
        var third = await Send(db, "r1", InboundKind.Text, "hello again");

        Assert.Contains(first, m => m is SendOutboundMessage { Kind: OutboundKind.CannedReply });
        Assert.DoesNotContain(second, m => m is SendOutboundMessage);
        Assert.DoesNotContain(third, m => m is SendOutboundMessage);
    }
}
