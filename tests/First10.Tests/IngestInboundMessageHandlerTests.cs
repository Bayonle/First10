using First10.Application.Ingest;
using First10.Domain.Channels;
using First10.Domain.Incidents;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace First10.Tests;

public class IngestInboundMessageHandlerTests
{
    private static First10DbContext NewDb() =>
        new(new DbContextOptionsBuilder<First10DbContext>()
            .UseInMemoryDatabase($"first10-{Guid.NewGuid()}")
            .Options);

    private static InboundChannelMessage Message(
        string sender = "persona-1",
        string? externalId = null,
        string text = "Accident dey happen for Mowe o!") =>
        new(ChannelKind.Local, sender, externalId ?? Guid.NewGuid().ToString("N"),
            InboundKind.Text, text, MediaRef: null, Location: null,
            OccurredAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task First_message_opens_provisional_ticket_with_timeline_entry()
    {
        await using var db = NewDb();

        var result = await IngestInboundMessageHandler.Handle(
            Message(), db, NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.NotNull(result);
        var ticket = Assert.Single(db.Tickets);
        Assert.Equal(TicketStatus.Provisional, ticket.Status); // D-007: ticket at session START
        Assert.Equal(ticket.Id, result!.TicketId);
        var entry = Assert.Single(db.TimelineEntries);
        Assert.Equal(ticket.Id, entry.TicketId);
        Assert.Equal(TimelineDirection.Inbound, entry.Direction);
    }

    [Fact]
    public async Task Second_message_from_same_sender_enriches_the_open_ticket()
    {
        await using var db = NewDb();

        await IngestInboundMessageHandler.Handle(Message(text: "crash!"), db, NullLogger.Instance, default);
        await db.SaveChangesAsync();
        await IngestInboundMessageHandler.Handle(Message(text: "two people trapped"), db, NullLogger.Instance, default);
        await db.SaveChangesAsync();

        Assert.Single(db.Tickets);
        Assert.Equal(2, db.TimelineEntries.Count());
    }

    [Fact]
    public async Task Different_senders_open_different_tickets()
    {
        await using var db = NewDb();

        await IngestInboundMessageHandler.Handle(Message(sender: "persona-1"), db, NullLogger.Instance, default);
        await db.SaveChangesAsync();
        await IngestInboundMessageHandler.Handle(Message(sender: "persona-2"), db, NullLogger.Instance, default);
        await db.SaveChangesAsync();

        // Dedup/merge into shared incidents is M2 (D-007); in M0 each reporter gets their own.
        Assert.Equal(2, db.Tickets.Count());
    }

    [Fact]
    public async Task Redelivered_message_is_dropped()
    {
        await using var db = NewDb();
        const string redeliveredId = "wamid-123";

        var first = await IngestInboundMessageHandler.Handle(
            Message(externalId: redeliveredId), db, NullLogger.Instance, default);
        await db.SaveChangesAsync();
        var second = await IngestInboundMessageHandler.Handle(
            Message(externalId: redeliveredId), db, NullLogger.Instance, default);
        await db.SaveChangesAsync();

        Assert.NotNull(first);
        Assert.Null(second); // D-005: every channel redelivers; dedup on (Channel, ExternalMessageId)
        Assert.Single(db.TimelineEntries);
    }
}
