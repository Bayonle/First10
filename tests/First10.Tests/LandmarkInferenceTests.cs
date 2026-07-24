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

/// <summary>
/// Landmark-inferred locations: selected from the closed gazetteer (never invented),
/// counted as located for dispatch/promotion, but NEVER trusted like a pin — a real
/// pin replaces the inference, and corroboration merges ignore inferred locations.
/// </summary>
public class LandmarkInferenceTests
{
    // ---- Gazetteer matching ----

    [Theory]
    [InlineData("accident dey happen for kara bridge o", "kara")]
    [InlineData("trailer don fall for CARRA", "kara")]
    [InlineData("crash before berger inward lagos", "berger")]
    [InlineData("okada accident for ibafo", "ibafo")]
    [InlineData("wahala for redemption camp axis", "redemption")]
    [InlineData("long bridge get accident now now", "longbridge")]
    public void Gazetteer_matches_landmark_aliases(string text, string expectedKey)
    {
        Assert.Equal(expectedKey, CorridorLandmarks.Match(text)?.Key);
    }

    [Theory]
    [InlineData("dem punch am for face, fight dey")] // "punch" alone must NOT match Magboro
    [InlineData("accident somewhere on the road")]
    [InlineData("people don camp for road")] // "camp" alone must not match Redemption Camp
    public void Gazetteer_does_not_false_positive_on_ambiguous_words(string text)
    {
        Assert.Null(CorridorLandmarks.Match(text));
    }

    [Fact]
    public void ByKey_rejects_unknown_keys()
    {
        Assert.Null(CorridorLandmarks.ByKey("lekki")); // off-corridor, not in the list
        Assert.NotNull(CorridorLandmarks.ByKey("kara"));
        Assert.NotNull(CorridorLandmarks.ByKey(" KARA ")); // normalized
    }

    [Fact]
    public void Heuristic_extractor_selects_the_landmark_key()
    {
        var result = new HeuristicIncidentExtractor()
            .ExtractAsync(new ExtractionInput("tanker don fall for kara bridge", null, "pidgin"), default)
            .Result;
        Assert.Equal("kara", result.LandmarkKey);
    }

    // ---- Pipeline semantics ----

    private static First10DbContext NewDb() =>
        new(new DbContextOptionsBuilder<First10DbContext>()
            .UseInMemoryDatabase($"first10-{Guid.NewGuid()}")
            .Options);

    private static async Task Send(First10DbContext db, string sender, InboundKind kind,
        string? text = null, GeoPoint? location = null)
    {
        await IngestInboundMessageHandler.Handle(
            new InboundChannelMessage(ChannelKind.Local, sender, Guid.NewGuid().ToString("N"),
                kind, text, null, location, DateTimeOffset.UtcNow),
            db, new HeuristicIntentClassifier(), new TestNullTranscriber(), new NullMediaStore(),
            new NullHasher(), new TriageOptions(), NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();
    }

    private sealed class NullHasher : IPerceptualHasher
    {
        public Task<ulong> HashAsync(Stream image, CancellationToken ct) => Task.FromResult(0UL);
    }

    private static async Task RunExtraction(First10DbContext db, IncidentTicket ticket)
    {
        var conversationId = db.TimelineEntries.First(e => e.TicketId == ticket.Id).ConversationId;
        await RunExtractionHandler.Handle(
            new RunExtraction(ticket.Id, conversationId),
            db, new HeuristicIncidentExtractor(), new NullMediaStore(),
            new TriageOptions { AllowUnapprovedTemplates = true }, NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Landmark_in_text_infers_an_approximate_location()
    {
        await using var db = NewDb();
        await Send(db, "rep-1", InboundKind.Text, "accident dey happen for kara bridge, motor don somersault");
        var ticket = db.Tickets.Single();
        await RunExtraction(db, ticket);

        Assert.Equal(LocationSource.LandmarkInferred, ticket.LocationSource);
        Assert.Equal("kara", ticket.LocationLandmark);
        Assert.NotNull(ticket.LocationResolvedAt);
        Assert.Equal(6.665, ticket.LocationLat!.Value, 3);
        Assert.Contains(db.TimelineEntries.Where(e => e.Direction == TimelineDirection.System).ToList(),
            e => e.Text!.Contains("Location inferred from landmark"));
    }

    [Fact]
    public async Task Real_pin_replaces_the_inference_and_upgrades_the_source()
    {
        await using var db = NewDb();
        await Send(db, "rep-1", InboundKind.Text, "accident for kara bridge");
        var ticket = db.Tickets.Single();
        await RunExtraction(db, ticket);
        Assert.Equal(LocationSource.LandmarkInferred, ticket.LocationSource);

        // The reporter figures out pins after all — exact location a bit off Kara.
        await Send(db, "rep-1", InboundKind.LocationPin, location: new GeoPoint(6.6612, 3.3795));

        Assert.Equal(LocationSource.Pin, ticket.LocationSource);
        Assert.Null(ticket.LocationLandmark);
        Assert.Equal(6.6612, ticket.LocationLat!.Value, 4);
        Assert.Contains(db.TimelineEntries.Where(e => e.Direction == TimelineDirection.System).ToList(),
            e => e.Text!.Contains("replaces inferred landmark"));
    }

    [Fact]
    public async Task Inference_never_overwrites_a_pin()
    {
        await using var db = NewDb();
        await Send(db, "rep-1", InboundKind.Text, "accident happening now");
        var ticket = db.Tickets.Single();
        await Send(db, "rep-1", InboundKind.LocationPin, location: new GeoPoint(6.7, 3.41));

        // A later message names a landmark — the pin stands.
        await Send(db, "rep-1", InboundKind.Text, "the one near kara bridge");
        await RunExtraction(db, ticket);

        Assert.Equal(LocationSource.Pin, ticket.LocationSource);
        Assert.Equal(6.7, ticket.LocationLat!.Value, 3);
    }

    [Fact]
    public async Task Inferred_location_plus_photo_promotes_without_a_pin()
    {
        await using var db = NewDb();
        await Send(db, "rep-1", InboundKind.Text, "tanker accident for kara bridge");
        var ticket = db.Tickets.Single();
        await Send(db, "rep-1", InboundKind.Image, text: null);
        Assert.Equal(TicketStatus.Provisional, ticket.Status); // photo alone: no location yet

        await RunExtraction(db, ticket); // extraction infers Kara → located → promotion rule met

        Assert.Equal(TicketStatus.Promoted, ticket.Status);
    }

    [Fact]
    public async Task Corroboration_merges_ignore_landmark_inferred_locations()
    {
        await using var db = NewDb();
        // Reporter A: landmark-inferred ticket at Kara.
        await Send(db, "rep-a", InboundKind.Text, "accident for kara bridge");
        var ticketA = db.Tickets.Single();
        await RunExtraction(db, ticketA);
        Assert.Equal(LocationSource.LandmarkInferred, ticketA.LocationSource);

        // Reporter B pins essentially the same coordinates. If inference had pin-level
        // trust this would merge — it must NOT (a landmark spans a kilometre).
        await Send(db, "rep-b", InboundKind.Text, "crash near the bridge");
        await Send(db, "rep-b", InboundKind.LocationPin, location: new GeoPoint(6.6651, 3.3831));

        var openTickets = db.Tickets.Where(t => t.Status != TicketStatus.Merged).Count();
        Assert.Equal(2, openTickets); // two separate incidents until a HUMAN merges judgment
        Assert.Equal(1, ticketA.ReporterCount);
    }
}
