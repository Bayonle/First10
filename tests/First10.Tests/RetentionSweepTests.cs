using First10.Application.Retention;
using First10.Domain;
using First10.Domain.Abstractions;
using First10.Domain.Incidents;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wolverine;

namespace First10.Tests;

/// <summary>
/// §7.1 retention: media past the window is deleted from the store, the timeline keeps
/// its words but loses the ref, every deletion is audit-logged, and orphan ticks from
/// previous deploys die silently.
/// </summary>
public class RetentionSweepTests
{
    private sealed class TrackingMediaStore : IMediaStore
    {
        public List<string> Deleted { get; } = [];
        public Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct) =>
            Task.FromResult("stub.jpg");
        public Task<Stream?> OpenReadAsync(string mediaRef, CancellationToken ct) =>
            Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string mediaRef, CancellationToken ct)
        {
            Deleted.Add(mediaRef);
            return Task.CompletedTask;
        }
        public string GetContentType(string mediaRef) => "image/jpeg";
    }

    private static First10DbContext NewDb() =>
        new(new DbContextOptionsBuilder<First10DbContext>()
            .UseInMemoryDatabase($"first10-{Guid.NewGuid()}")
            .Options);

    private static TimelineEntry Entry(string? mediaRef, DateTimeOffset occurredAt, Guid? ticketId = null) => new()
    {
        Id = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        TicketId = ticketId,
        Direction = TimelineDirection.Inbound,
        Kind = mediaRef is null ? TimelineEntryKind.Text : TimelineEntryKind.Image,
        Text = mediaRef is null ? "text stays forever" : null,
        MediaRef = mediaRef,
        OccurredAt = occurredAt,
    };

    private static async Task<(First10DbContext Db, Guid Chain)> DbWithChain()
    {
        var db = NewDb();
        var chain = Guid.NewGuid();
        db.SystemState.Add(new SystemStateEntry { Key = RetentionSweepHandler.ChainKey, Value = chain.ToString() });
        await db.SaveChangesAsync();
        return (db, chain);
    }

    [Fact]
    public async Task Expired_media_is_deleted_ref_nulled_and_audit_logged()
    {
        var (db, chain) = await DbWithChain();
        var ticketId = Guid.NewGuid();
        var old = Entry("old-photo.jpg", DateTimeOffset.UtcNow.AddDays(-40), ticketId);
        var oldVoice = Entry("old-voice.ogg", DateTimeOffset.UtcNow.AddDays(-31));
        var fresh = Entry("fresh-photo.jpg", DateTimeOffset.UtcNow.AddDays(-5));
        var oldText = Entry(null, DateTimeOffset.UtcNow.AddDays(-100));
        db.TimelineEntries.AddRange(old, oldVoice, fresh, oldText);
        await db.SaveChangesAsync();

        var store = new TrackingMediaStore();
        await RetentionSweepHandler.Handle(new RetentionSweepDue(chain), db, store,
            new RetentionOptions { MediaRetentionDays = 30 }, Substitute.For<IMessageBus>(),
            NullLogger.Instance, CancellationToken.None);

        Assert.Equal(["old-photo.jpg", "old-voice.ogg"], store.Deleted.Order());
        Assert.Null(old.MediaRef);
        Assert.Null(oldVoice.MediaRef);
        Assert.Equal("fresh-photo.jpg", fresh.MediaRef); // inside the window — untouched
        Assert.Equal("text stays forever", oldText.Text); // words are the incident record

        var audits = db.AccessLogs.Where(a => a.Kind == AccessKind.MediaDeleted).ToList();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => Assert.Equal("retention-job", a.Who));
        Assert.Contains(audits, a => a.MediaRef == "old-photo.jpg" && a.TicketId == ticketId);
    }

    [Fact]
    public async Task Sweep_reschedules_itself_on_the_same_chain()
    {
        var (db, chain) = await DbWithChain();
        var bus = Substitute.For<IMessageBus>();

        await RetentionSweepHandler.Handle(new RetentionSweepDue(chain), db, new TrackingMediaStore(),
            new RetentionOptions { SweepIntervalHours = 6 }, bus, NullLogger.Instance, CancellationToken.None);

        // ScheduleAsync is an extension method; assert on the message it pushed through the bus.
        var scheduled = bus.ReceivedCalls()
            .SelectMany(c => c.GetArguments())
            .OfType<RetentionSweepDue>()
            .ToList();
        Assert.Single(scheduled);
        Assert.Equal(chain, scheduled[0].ChainId);
    }

    [Fact]
    public async Task Orphan_tick_from_a_previous_deploy_dies_without_sweeping_or_rescheduling()
    {
        var (db, _) = await DbWithChain();
        db.TimelineEntries.Add(Entry("old-photo.jpg", DateTimeOffset.UtcNow.AddDays(-40)));
        await db.SaveChangesAsync();

        var store = new TrackingMediaStore();
        var bus = Substitute.For<IMessageBus>();
        await RetentionSweepHandler.Handle(new RetentionSweepDue(Guid.NewGuid()), db, store,
            new RetentionOptions(), bus, NullLogger.Instance, CancellationToken.None);

        Assert.Empty(store.Deleted);
        Assert.Empty(bus.ReceivedCalls()); // no reschedule — the orphan chain is dead
    }
}
