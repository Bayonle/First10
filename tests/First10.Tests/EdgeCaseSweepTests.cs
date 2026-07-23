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

/// <summary>Regressions from the browser-driven edge-case sweep (22 Jul).</summary>
public class EdgeCaseSweepTests
{
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

    [Fact]
    public async Task Pin_first_report_is_asked_for_a_photo_not_the_pin_again()
    {
        await using var db = NewDb();

        var outgoing = await Send(db, InboundKind.LocationPin, location: new GeoPoint(6.743, 3.430));

        var ticket = Assert.Single(db.Tickets);
        Assert.NotNull(ticket.LocationResolvedAt);
        // "Location received … please also send a photo" — not the full photo+pin ask.
        Assert.Contains(outgoing, m => m is SendOutboundMessage { Kind: OutboundKind.PinReceivedAck });
        Assert.DoesNotContain(outgoing, m => m is SendOutboundMessage { Kind: OutboundKind.ElicitationChallenge });
    }

    [Fact]
    public async Task Oversized_text_is_truncated_not_dead_lettered()
    {
        await using var db = NewDb();
        var longText = "accident for road " + new string('a', 10_000);

        var outgoing = await Send(db, InboundKind.Text, longText);

        // Report survives: entry stored (truncated to the column cap), reply sent.
        var entry = db.TimelineEntries.Single(e => e.Direction == TimelineDirection.Inbound);
        Assert.True(entry.Text!.Length <= 8192, $"stored length {entry.Text.Length}");
        Assert.Single(db.Tickets);
        Assert.Contains(outgoing, m => m is SendOutboundMessage { Kind: OutboundKind.ElicitationChallenge });
    }

    [Fact]
    public async Task Emoji_pidgin_report_triages_and_replies_in_pidgin()
    {
        await using var db = NewDb();

        var outgoing = await Send(db, InboundKind.Text,
            "🚨🚨 accident o!! danfo don somersault 😭😭 people dey ground");

        var ticket = Assert.Single(db.Tickets);
        Assert.Equal("pidgin", ticket.Language);
        Assert.Contains(outgoing, m => m is SendOutboundMessage { Language: "pidgin" });
    }
}
