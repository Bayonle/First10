using First10.Domain.Incidents;
using First10.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace First10.Api.Controllers;

public sealed record TicketListItem(
    Guid Id,
    TicketStatus Status,
    First10.Domain.Triage.Disposition Disposition,
    First10.Domain.Triage.EvidenceLevel Evidence,
    SeverityTier? Severity,
    string? CasualtyEstimate,
    int ReporterCount,
    string? Language,
    string? Flags,
    string Summary,
    DateTimeOffset? LocationResolvedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TimelineEntryDto(
    Guid Id,
    Guid ConversationId,
    TimelineDirection Direction,
    TimelineEntryKind Kind,
    string? Text,
    string? MediaRef,
    string? TranscriptText,
    DateTimeOffset OccurredAt);

/// <summary>
/// Read API for the dispatcher console. M0: unauthenticated reads for the walking
/// skeleton — OIDC lands in M4 before any pilot traffic (D-013).
/// </summary>
[ApiController]
[Route("api/tickets")]
public class TicketsController(First10DbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<TicketListItem>> List(CancellationToken ct) =>
        await db.Tickets
            .Where(t => t.Status != TicketStatus.Merged) // merged shells live on inside their survivor
            .OrderByDescending(t => t.UpdatedAt)
            .Take(100)
            .Select(t => new TicketListItem(
                t.Id, t.Status, t.Disposition, t.Evidence, t.Severity, t.CasualtyEstimate,
                t.ReporterCount, t.Language, t.Flags,
                t.Summary, t.LocationResolvedAt, t.CreatedAt, t.UpdatedAt))
            .ToListAsync(ct);

    [HttpGet("{id:guid}/timeline")]
    public async Task<ActionResult<IReadOnlyList<TimelineEntryDto>>> Timeline(Guid id, CancellationToken ct)
    {
        var exists = await db.Tickets.AnyAsync(t => t.Id == id, ct);
        if (!exists)
        {
            return NotFound();
        }

        return await db.TimelineEntries
            .Where(e => e.TicketId == id)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new TimelineEntryDto(
                e.Id, e.ConversationId, e.Direction, e.Kind, e.Text, e.MediaRef,
                e.TranscriptText, e.OccurredAt))
            .ToListAsync(ct);
    }
}
