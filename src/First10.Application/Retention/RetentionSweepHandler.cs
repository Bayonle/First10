using First10.Domain;
using First10.Domain.Abstractions;
using First10.Domain.Incidents;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace First10.Application.Retention;

/// <summary>Self-perpetuating retention sweep tick (D-002: scheduled messages, no cron).</summary>
public sealed record RetentionSweepDue(Guid ChainId);

public sealed class RetentionOptions
{
    /// <summary>Days evidence media (photos, voice notes) is kept. PROVISIONAL — the
    /// lawyer's NDPA review sets the final number before soft launch (G4).</summary>
    public int MediaRetentionDays { get; set; } = 30;

    public int SweepIntervalHours { get; set; } = 6;
}

/// <summary>
/// Deletes media past the retention window (§7.1): blob removed from the store, the
/// timeline entry keeps its text/transcript but loses the media ref, and every deletion
/// writes an audit row. Text, transcripts, and audit tables are NOT touched — they are
/// the incident record; media is what carries faces and voices.
///
/// Chain guard: each process start rotates a chain id and schedules a fresh tick, so
/// scheduled ticks from previous deploys (still durable in the outbox) die as no-ops
/// instead of stacking into ever-more-frequent sweeps.
/// </summary>
public static class RetentionSweepHandler
{
    public const string ChainKey = "retention-chain";

    public static async Task Handle(
        RetentionSweepDue message,
        First10DbContext db,
        IMediaStore mediaStore,
        RetentionOptions options,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct)
    {
        var chain = await db.SystemState.SingleOrDefaultAsync(s => s.Key == ChainKey, ct);
        if (chain is null || chain.Value != message.ChainId.ToString())
        {
            return; // orphan tick from a previous deploy — let it die
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.MediaRetentionDays);
        var expired = await db.TimelineEntries
            .Where(e => e.MediaRef != null && e.OccurredAt < cutoff)
            .ToListAsync(ct);

        foreach (var entry in expired)
        {
            await mediaStore.DeleteAsync(entry.MediaRef!, ct); // idempotent — safe to re-run
            db.AccessLogs.Add(new AccessLogRecord
            {
                Id = Guid.NewGuid(),
                Kind = AccessKind.MediaDeleted,
                Who = "retention-job",
                MediaRef = entry.MediaRef,
                TicketId = entry.TicketId,
                At = DateTimeOffset.UtcNow,
            });
            entry.MediaRef = null; // console renders a "[media expired]" placeholder
        }

        if (expired.Count > 0)
        {
            logger.LogInformation("Retention sweep deleted {Count} media blobs past {Days}d window",
                expired.Count, options.MediaRetentionDays);
        }

        await db.SaveChangesAsync(ct);
        await bus.ScheduleAsync(
            new RetentionSweepDue(message.ChainId), TimeSpan.FromHours(options.SweepIntervalHours));
    }
}
