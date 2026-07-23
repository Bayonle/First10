using First10.Domain.Incidents;
using First10.Domain.Triage;
using First10.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace First10.Api.Controllers;

public sealed record StatsDto(
    int ReportsTotal,
    int VerifiedReports,          // promoted or auto-verified or dispatched
    int MultiReporterIncidents,
    int OpenQueueDepth,
    int DispatchedCount,
    double? MedianTimeToDispatchMinutes,
    double? MedianInstructionLatencySeconds,
    double InstructionCoverageRate,   // instructed / verified   (target ≥ 0.9)
    double LoopClosureRate,           // dispatched / verified   (target ≥ 0.8)
    double? FalsePositiveRate,        // false / (false + real)  (target < 0.05)
    int OutcomesMarked);

/// <summary>
/// The §8.3 KPI numbers, computed straight off the ticket ledger — the same figures
/// the panel deck's evaluation slide needs. Baseline time-to-dispatch (the ~25min
/// claim) is measured with FRSC before soft launch (M5) and compared externally.
/// </summary>
[ApiController]
[Authorize(Policy = "Dispatcher")]
[Route("api/stats")]
public class StatsController(First10DbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<StatsDto> Get(CancellationToken ct)
    {
        var tickets = await db.Tickets
            .Where(t => t.Status != TicketStatus.Merged)
            .Select(t => new
            {
                t.Status, t.Disposition, t.Dispatch, t.Outcome, t.ReporterCount,
                t.CreatedAt, t.DispatchedAt, t.InstructionSentAt,
            })
            .ToListAsync(ct);

        var verified = tickets.Where(t =>
            t.Status is TicketStatus.Promoted or TicketStatus.Closed
            || t.Disposition == Disposition.AutoVerify
            || t.Dispatch != DispatchState.None).ToList();

        var dispatched = tickets.Where(t => t.DispatchedAt is not null).ToList();
        var instructed = verified.Where(t => t.InstructionSentAt is not null).ToList();

        var falseCount = tickets.Count(t => t.Outcome == TicketOutcome.False);
        var realCount = tickets.Count(t => t.Outcome == TicketOutcome.Real);

        return new StatsDto(
            ReportsTotal: tickets.Count,
            VerifiedReports: verified.Count,
            MultiReporterIncidents: tickets.Count(t => t.ReporterCount >= 2),
            OpenQueueDepth: tickets.Count(t =>
                t.Status is TicketStatus.Provisional or TicketStatus.Promoted
                && t.Dispatch == DispatchState.None),
            DispatchedCount: dispatched.Count,
            MedianTimeToDispatchMinutes: Median(dispatched
                .Select(t => (t.DispatchedAt!.Value - t.CreatedAt).TotalMinutes)),
            MedianInstructionLatencySeconds: Median(tickets
                .Where(t => t.InstructionSentAt is not null)
                .Select(t => (t.InstructionSentAt!.Value - t.CreatedAt).TotalSeconds)),
            InstructionCoverageRate: verified.Count == 0 ? 0 : (double)instructed.Count / verified.Count,
            LoopClosureRate: verified.Count == 0 ? 0 : (double)dispatched.Count / verified.Count,
            FalsePositiveRate: falseCount + realCount == 0 ? null : (double)falseCount / (falseCount + realCount),
            OutcomesMarked: falseCount + realCount + tickets.Count(t => t.Outcome == TicketOutcome.Unverifiable));
    }

    private static double? Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0) return null;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
