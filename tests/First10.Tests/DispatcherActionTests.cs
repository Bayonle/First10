using First10.Application.Dispatch;
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

/// <summary>M3: loop-closure only from explicit dispatcher actions (R1e), to every reporter.</summary>
public class DispatcherActionTests
{
    private sealed class NullHasher : IPerceptualHasher
    {
        public Task<ulong> HashAsync(Stream image, CancellationToken ct) => Task.FromResult(0UL);
    }

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
            db, new HeuristicIntentClassifier(), new TestNullTranscriber(), new NullMediaStore(), new NullHasher(),
            new TriageOptions(), NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();
    }

    private static async Task<OutgoingMessages> Act(First10DbContext db, Guid ticketId, DispatcherActionKind kind)
    {
        var result = await DispatcherActionHandler.Handle(
            new DispatcherAction(ticketId, kind, "officer-test"),
            db, new HeuristicTimelineSummarizer(), NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();
        return result;
    }

    private static readonly GeoPoint Kara = new(6.665, 3.383);
    private static readonly GeoPoint Kara100m = new(6.6659, 3.383);

    private static async Task<IncidentTicket> TwoReporterIncident(First10DbContext db)
    {
        await Send(db, "rep-a", InboundKind.Text, "trailer don fall for kara bridge, accident");
        await Send(db, "rep-a", InboundKind.LocationPin, location: Kara);
        await Send(db, "rep-b", InboundKind.LocationPin, location: Kara100m);
        return db.Tickets.Single(t => t.Status != TicketStatus.Merged);
    }

    [Fact]
    public async Task Dispatch_notifies_every_reporter_and_generates_crew_briefing()
    {
        await using var db = NewDb();
        var ticket = await TwoReporterIncident(db);

        var outgoing = await Act(db, ticket.Id, DispatcherActionKind.Dispatched);

        Assert.Equal(DispatchState.Dispatched, ticket.Dispatch);
        Assert.NotNull(ticket.DispatchedAt);
        Assert.NotNull(ticket.CrewBriefing); // generated at dispatch (paper §1.4)
        // Loop closure reaches BOTH reporters
        Assert.Equal(2, outgoing.OfType<SendOutboundMessage>()
            .Count(m => m.Kind == OutboundKind.DispatchedNotice));
        // Time-to-dispatch metric recorded
        Assert.Contains(db.TimelineEntries, e =>
            e.Direction == TimelineDirection.System && e.Text!.Contains("time-to-dispatch"));
    }

    [Fact]
    public async Task Transitions_are_strictly_ordered_and_double_clicks_are_silent()
    {
        await using var db = NewDb();
        var ticket = await TwoReporterIncident(db);

        // Arrive before dispatch → ignored, no messages
        var premature = await Act(db, ticket.Id, DispatcherActionKind.Arrived);
        Assert.Empty(premature);
        Assert.Equal(DispatchState.None, ticket.Dispatch);

        await Act(db, ticket.Id, DispatcherActionKind.Dispatched);
        // Double-click on dispatch → ignored, reporters NOT double-messaged
        var doubleClick = await Act(db, ticket.Id, DispatcherActionKind.Dispatched);
        Assert.Empty(doubleClick);

        await Act(db, ticket.Id, DispatcherActionKind.Arrived);
        var final = await Act(db, ticket.Id, DispatcherActionKind.Transported);

        Assert.Equal(DispatchState.Transported, ticket.Dispatch);
        Assert.Equal(TicketStatus.Closed, ticket.Status);
        Assert.Equal(2, final.OfType<SendOutboundMessage>()
            .Count(m => m.Kind == OutboundKind.TransportedNotice));
        // Conversations freed for future incidents
        Assert.All(db.Conversations, c => Assert.Null(c.ActiveTicketId));
    }

    [Fact]
    public async Task False_outcome_rejects_ticket_and_downgrades_reporter_trust()
    {
        await using var db = NewDb();
        var ticket = await TwoReporterIncident(db);

        await DispatcherActionHandler.Handle(
            new MarkOutcome(ticket.Id, TicketOutcome.False, "officer-test", "staged photo"),
            db, new TriageOptions(), NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(TicketStatus.Rejected, ticket.Status);
        Assert.Equal(TicketOutcome.False, ticket.Outcome);
        // Sticky reputation hit for every contributing reporter (D-008 feedback loop)
        Assert.Equal(2, db.ReporterReputations.Count(r => r.Trust == TrustLevel.Low));
    }

    [Fact]
    public async Task Late_reporter_of_dispatched_incident_gets_already_handled()
    {
        await using var db = NewDb();
        var ticket = await TwoReporterIncident(db);
        await Act(db, ticket.Id, DispatcherActionKind.Dispatched);

        // Third reporter pins the same spot after dispatch
        var outgoing = new OutgoingMessages();
        await Send(db, "rep-late", InboundKind.LocationPin, location: new GeoPoint(6.6655, 3.3831));
        var late = db.Conversations.Single(c => c.ExternalUserId == "rep-late");

        // No new incident; reporter told it's being handled; session left free
        Assert.Single(db.Tickets, t => t.Status != TicketStatus.Merged);
        Assert.Null(late.ActiveTicketId);
        Assert.Contains(db.TimelineEntries, e =>
            e.Direction == TimelineDirection.System && e.Text!.Contains("Late reporter"));
    }
}
