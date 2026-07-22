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
/// Regression for the "treadmill" bug: a reporter messaging every few minutes kept
/// resetting the inactivity clock, so an ancient ticket swallowed everything forever.
/// The session-age cap ends the session regardless of message cadence.
/// </summary>
public class SessionMaxAgeTests
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

    private static async Task Send(First10DbContext db, string text)
    {
        await IngestInboundMessageHandler.Handle(
            new InboundChannelMessage(ChannelKind.Local, "r1", Guid.NewGuid().ToString("N"),
                InboundKind.Text, text, null, null, DateTimeOffset.UtcNow),
            db, new HeuristicIntentClassifier(), new NullMediaStore(), new NullHasher(),
            new TriageOptions(), NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Old_session_splits_even_when_reporter_messages_frequently()
    {
        await using var db = NewDb();

        await Send(db, "accident for road");

        // Backdate the TICKET's age past the cap, but keep the conversation "active"
        // (recent inbound) — the treadmill scenario.
        var ticket = db.Tickets.Single();
        ticket.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-90);
        db.Conversations.Single().LastInboundAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        await db.SaveChangesAsync();

        await Send(db, "accident for Kara bridge, trailer don fall");

        Assert.Equal(2, db.Tickets.Count()); // fresh incident, not swallowed
        var stale = db.Tickets.OrderBy(t => t.CreatedAt).First();
        Assert.Equal(TicketStatus.ExpiredUnverified, stale.Status); // challenge never answered
        Assert.Contains(db.TimelineEntries, e =>
            e.TicketId == stale.Id && e.Direction == TimelineDirection.System
            && e.Text!.Contains("older than"));
    }

    [Fact]
    public async Task Young_active_session_still_absorbs_updates()
    {
        await using var db = NewDb();

        await Send(db, "accident for road");
        db.Conversations.Single().LastInboundAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        await db.SaveChangesAsync();

        await Send(db, "one person no fit move");

        Assert.Single(db.Tickets); // within age + inactivity windows → update, not new incident
    }
}
