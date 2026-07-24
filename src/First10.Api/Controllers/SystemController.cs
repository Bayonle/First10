using First10.Domain.Triage;
using First10.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace First10.Api.Controllers;

public sealed record FloodState(bool Active, int TicketsInWindow, int Threshold, int WindowMinutes);

[ApiController]
[Authorize(Policy = "Dispatcher")]
[Route("api/system")]
public class SystemController(First10DbContext db, TriageOptions options) : ControllerBase
{
    /// <summary>R11 flood banner state for the console.</summary>
    [HttpGet("flood")]
    public async Task<FloodState> Flood(CancellationToken ct)
    {
        var windowStart = DateTimeOffset.UtcNow.AddMinutes(-options.FloodWindowMinutes);
        var count = await db.Tickets.CountAsync(t => t.CreatedAt >= windowStart, ct);
        return new FloodState(
            count >= options.FloodDistinctConversations,
            count,
            options.FloodDistinctConversations,
            options.FloodWindowMinutes);
    }

    /// <summary>
    /// The ENFORCED corridor geofence (centerline + buffer) straight from triage config —
    /// the console map draws exactly what Stage 0 checks, so when the FRSC-verified
    /// waypoints land (M5) the map updates with the config, no frontend change.
    /// </summary>
    [HttpGet("corridor")]
    public object Corridor() => new
    {
        bufferKm = options.CorridorBufferKm,
        centerline = options.CorridorCenterline.Select(p => new { lat = p.Latitude, lng = p.Longitude }),
    };

    /// <summary>
    /// Dev-only: clears the dead-letter table after investigation. A permanently red
    /// banner about historic, root-caused failures is alarm fatigue — the signal only
    /// works if it returns to silence once handled. In pilot, clearing follows the
    /// ops runbook (replay first if the envelopes are real reports); this endpoint is
    /// unreachable outside Development (D-006 gate).
    /// </summary>
    [HttpPost("dead-letters/purge")]
    [First10.Api.Filters.DevelopmentOnly]
    public async Task<ActionResult<object>> PurgeDeadLetters(CancellationToken ct)
    {
        foreach (var table in new[]
                 { "wolverine_dead_letters", "public.wolverine_dead_letters", "wolverine.wolverine_dead_letters" })
        {
            try
            {
                var deleted = await db.Database.ExecuteSqlRawAsync($"DELETE FROM {table}", ct);
                return Ok(new { deleted });
            }
            catch (Exception)
            {
                // try the next location
            }
        }
        return Ok(new { deleted = 0 });
    }

    /// <summary>
    /// Dead-lettered envelopes = reports the pipeline gave up on (D-008: never silent).
    /// The console shows a hard red banner whenever this is non-zero; recovery is a
    /// Wolverine dead-letter replay by an engineer.
    /// </summary>
    [HttpGet("dead-letters")]
    public async Task<ActionResult<object>> DeadLetters(CancellationToken ct)
    {
        // Wolverine's schema placement varies by config; probe the known locations.
        foreach (var table in new[]
                 { "wolverine_dead_letters", "public.wolverine_dead_letters", "wolverine.wolverine_dead_letters" })
        {
            try
            {
                var count = await db.Database
                    .SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM {table}")
                    .SingleAsync(ct);
                return Ok(new { count });
            }
            catch (Exception)
            {
                // try the next location
            }
        }

        // Table absent (fresh db, non-Postgres test provider) — report unknown, not zero.
        return Ok(new { count = (int?)null });
    }
}
