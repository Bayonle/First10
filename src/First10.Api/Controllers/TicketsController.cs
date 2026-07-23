using First10.Domain.Incidents;
using First10.Infrastructure.Media;
using First10.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
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
    double? LocationLat,
    double? LocationLng,
    DispatchState Dispatch,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? ArrivedAt,
    DateTimeOffset? TransportedAt,
    TicketOutcome? Outcome,
    string? TimelineDigest,
    string? Contradictions,
    string? CrewBriefing,
    string? PendingAsk,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TimelineEntryDto(
    Guid Id,
    Guid ConversationId,
    TimelineDirection Direction,
    TimelineEntryKind Kind,
    string? Text,
    string? MediaRef,
    string? MediaUrl,
    string? TranscriptText,
    DateTimeOffset OccurredAt);

/// <summary>Read API for the dispatcher console. Dispatcher role required (M4).</summary>
[ApiController]
[Authorize(Policy = "Dispatcher")]
[Route("api/tickets")]
public class TicketsController(First10DbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<TicketListItem>> List(CancellationToken ct)
    {
        // Actionability order: what the dispatcher must ACT on outranks recency.
        //   0 = needs a dispatch decision (open, undispatched) — strongest first
        //   1 = in progress (dispatched/arrived)   2 = expired-unverified   3 = closed/rejected
        var tickets = await db.Tickets
            .Where(t => t.Status != TicketStatus.Merged) // merged shells live on inside their survivor
            .OrderBy(t =>
                t.Status == TicketStatus.Closed || t.Status == TicketStatus.Rejected ? 3
                : t.Status == TicketStatus.ExpiredUnverified ? 2
                : t.Dispatch != DispatchState.None ? 1
                : 0)
            .ThenByDescending(t => t.Disposition)
            .ThenByDescending(t => t.Severity)
            .ThenBy(t => t.CreatedAt) // oldest waiting first — nobody rots at the bottom
            .Take(100)
            .ToListAsync(ct);

        return tickets.Select(t => new TicketListItem(
            t.Id, t.Status, t.Disposition, t.Evidence, t.Severity, t.CasualtyEstimate,
            t.ReporterCount, t.Language, t.Flags,
            t.Summary, t.LocationResolvedAt, t.LocationLat, t.LocationLng,
            t.Dispatch, t.DispatchedAt, t.ArrivedAt, t.TransportedAt,
            t.Outcome, t.TimelineDigest, t.Contradictions, t.CrewBriefing,
            PendingAsk(t),
            t.CreatedAt, t.UpdatedAt)).ToList();
    }

    /// <summary>What the session is still waiting on from the reporter — the console's
    /// "pin requested, awaiting reply" live state.</summary>
    private static string? PendingAsk(IncidentTicket t)
    {
        if (t.Status is not (TicketStatus.Provisional or TicketStatus.Promoted)) return null;
        var needsScene = t.Evidence <= First10.Domain.Triage.EvidenceLevel.TextOnly;
        var needsPin = t.LocationResolvedAt is null;
        return (needsScene, needsPin) switch
        {
            (true, true) => "awaiting photo + location pin",
            (false, true) => "awaiting location pin",
            (true, false) => "awaiting scene photo",
            _ => null,
        };
    }

    /// <summary>
    /// The evidence view. Fetching it is an audited access (§7.1): one TicketViewed row,
    /// plus one MediaUrlIssued row per signed media URL minted into the response.
    /// </summary>
    [HttpGet("{id:guid}/timeline")]
    public async Task<ActionResult<IReadOnlyList<TimelineEntryDto>>> Timeline(
        Guid id, [FromServices] MediaUrlSigner signer, CancellationToken ct)
    {
        var exists = await db.Tickets.AnyAsync(t => t.Id == id, ct);
        if (!exists)
        {
            return NotFound();
        }

        var entries = await db.TimelineEntries
            .Where(e => e.TicketId == id)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(ct);

        var who = User.Identity?.Name ?? "dev-console"; // real identity once OIDC lands
        var now = DateTimeOffset.UtcNow;
        db.AccessLogs.Add(new AccessLogRecord
        {
            Id = Guid.NewGuid(), Kind = AccessKind.TicketViewed, Who = who, TicketId = id, At = now,
        });

        var dtos = new List<TimelineEntryDto>(entries.Count);
        foreach (var e in entries)
        {
            string? mediaUrl = null;
            if (e.MediaRef is not null)
            {
                var (expires, sig) = signer.Issue(e.MediaRef, now);
                mediaUrl = $"/api/media/{Uri.EscapeDataString(e.MediaRef)}?e={expires}&s={sig}";
                db.AccessLogs.Add(new AccessLogRecord
                {
                    Id = Guid.NewGuid(), Kind = AccessKind.MediaUrlIssued, Who = who,
                    MediaRef = e.MediaRef, TicketId = id, At = now,
                });
            }

            dtos.Add(new TimelineEntryDto(
                e.Id, e.ConversationId, e.Direction, e.Kind, e.Text, e.MediaRef, mediaUrl,
                e.TranscriptText, e.OccurredAt));
        }

        await db.SaveChangesAsync(ct);
        return dtos;
    }
}
