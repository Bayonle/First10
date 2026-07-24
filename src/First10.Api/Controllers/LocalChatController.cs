using First10.Api.Filters;
using First10.Domain.Channels;
using First10.Domain.Abstractions;
using First10.Domain.Incidents;
using First10.Infrastructure.Media;
using First10.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace First10.Api.Controllers;

public sealed record LocalChatMessageRequest(
    string SenderId,
    string? Text,
    string? MediaRef,
    double? Latitude,
    double? Longitude,
    LocalMessageKind Kind = LocalMessageKind.Text);

public enum LocalMessageKind
{
    Text = 0,
    Image = 1,
    Voice = 2,
    LocationPin = 3,
}

public sealed record ConversationEntryDto(
    Guid Id,
    Guid? TicketId,
    TimelineDirection Direction,
    TimelineEntryKind Kind,
    string? Text,
    string? MediaRef,
    string? MediaUrl,
    DateTimeOffset OccurredAt);

/// <summary>
/// The Local channel adapter (D-006): the dev cockpit posts here, and messages enter
/// the exact same pipeline as WhatsApp/Telegram — normalized envelope, durable queue,
/// same handlers. Nothing downstream can tell it's fake.
/// </summary>
[ApiController]
[Route("api/local-chat")]
[DevelopmentOnly] // D-006 hard gate — see DevelopmentOnlyAttribute
public class LocalChatController(IMessageBus bus) : ControllerBase
{
    [HttpPost("messages")]
    public async Task<IActionResult> Send([FromBody] LocalChatMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SenderId))
        {
            return BadRequest("SenderId is required.");
        }

        if (!Enum.IsDefined(request.Kind))
        {
            return BadRequest($"Unknown message kind {(int)request.Kind}.");
        }

        if (request.Kind == LocalMessageKind.Text && string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest("Text is required for text messages.");
        }

        if (request.Kind is LocalMessageKind.Image or LocalMessageKind.Voice && string.IsNullOrWhiteSpace(request.MediaRef))
        {
            return BadRequest("MediaRef is required for media messages.");
        }

        if (request.Kind == LocalMessageKind.LocationPin && (request.Latitude is null || request.Longitude is null))
        {
            return BadRequest("Latitude/Longitude are required for location pins.");
        }

        var envelope = new InboundChannelMessage(
            Channel: ChannelKind.Local,
            ExternalUserId: request.SenderId.Trim(),
            ExternalMessageId: Guid.NewGuid().ToString("N"), // local provider mints its own ids
            Kind: (InboundKind)request.Kind,
            Text: request.Text,
            MediaRef: request.MediaRef,
            Location: request is { Latitude: { } lat, Longitude: { } lng } ? new GeoPoint(lat, lng) : null,
            OccurredAt: DateTimeOffset.UtcNow);

        // Same contract as every real adapter: publish and return fast.
        await bus.PublishAsync(envelope);
        return Accepted();
    }

    /// <summary>
    /// Upload cockpit media (image/voice/video) before sending the message that
    /// references it. This endpoint stands in for the real adapters' media-download
    /// step, so it goes through the same D-009 gate: images are face-blurred in memory
    /// before the store ever sees them, and videos become blurred contact sheets — the
    /// unblurred bytes exist only inside this request scope. 16MB = WhatsApp's media cap.
    /// </summary>
    [HttpPost("media")]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<IActionResult> UploadMedia(
        IFormFile file, [FromServices] SecureMediaIngest ingest, CancellationToken ct)
    {
        if (file.Length == 0) return BadRequest("Empty file.");

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await ingest.IngestAsync(stream, file.ContentType, ct);
            return Ok(new { result.MediaRef });
        }
        catch (NotSupportedException e)
        {
            return BadRequest(e.Message);
        }
    }

    /// <summary>
    /// Dev-only clean slate: deletes ALL Local-channel data (conversations, their
    /// timeline entries, tickets with no real-channel contributions, media rows,
    /// local reporter reputations). Real channels (Telegram/WhatsApp) are untouched —
    /// a ticket with even one real-channel message survives. Audit tables are never
    /// deleted. Everything purged is synthetic and regenerable via scenarios.
    /// </summary>
    [HttpPost("purge")]
    public async Task<IActionResult> PurgeLocalData([FromServices] First10DbContext db, CancellationToken ct)
    {
        var localConversationIds = await db.Conversations
            .Where(c => c.Channel == ChannelKind.Local)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var candidateTicketIds = await db.TimelineEntries
            .Where(e => e.TicketId != null && localConversationIds.Contains(e.ConversationId))
            .Select(e => e.TicketId!.Value)
            .Distinct()
            .ToListAsync(ct);

        // Safety: only tickets whose EVERY contribution is Local die with the purge.
        var protectedTicketIds = await db.TimelineEntries
            .Where(e => e.TicketId != null && candidateTicketIds.Contains(e.TicketId.Value)
                        && !localConversationIds.Contains(e.ConversationId))
            .Select(e => e.TicketId!.Value)
            .Distinct()
            .ToListAsync(ct);
        var doomedTicketIds = candidateTicketIds.Except(protectedTicketIds).ToList();

        var entries = await db.TimelineEntries
            .Where(e => localConversationIds.Contains(e.ConversationId)
                        || (e.TicketId != null && doomedTicketIds.Contains(e.TicketId.Value)))
            .ExecuteDeleteAsync(ct);
        await db.MediaAssets
            .Where(m => m.TicketId != null && doomedTicketIds.Contains(m.TicketId.Value))
            .ExecuteDeleteAsync(ct);
        var tickets = await db.Tickets.Where(t => doomedTicketIds.Contains(t.Id)).ExecuteDeleteAsync(ct);
        var conversations = await db.Conversations
            .Where(c => c.Channel == ChannelKind.Local).ExecuteDeleteAsync(ct);
        await db.ReporterReputations.Where(r => r.Channel == ChannelKind.Local).ExecuteDeleteAsync(ct);

        return Ok(new { conversations, tickets, entries, protectedTickets = protectedTicketIds.Count });
    }

    /// <summary>The cockpit's conversation view: both directions, for one persona.</summary>
    [HttpGet("{senderId}/timeline")]
    public async Task<IReadOnlyList<ConversationEntryDto>> ConversationTimeline(
        string senderId, [FromServices] First10DbContext db, [FromServices] MediaUrlSigner signer, CancellationToken ct)
    {
        var entries = await db.Conversations
            .Where(c => c.Channel == ChannelKind.Local && c.ExternalUserId == senderId)
            .Join(db.TimelineEntries, c => c.Id, e => e.ConversationId, (c, e) => e)
            .Where(e => e.Direction != TimelineDirection.System) // reporter never sees system notes
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        return entries.Select(e =>
        {
            string? mediaUrl = null;
            if (e.MediaRef is not null)
            {
                var (expires, sig) = signer.Issue(e.MediaRef, now);
                mediaUrl = $"/api/media/{Uri.EscapeDataString(e.MediaRef)}?e={expires}&s={sig}";
            }
            return new ConversationEntryDto(
                e.Id, e.TicketId, e.Direction, e.Kind, e.Text, e.MediaRef, mediaUrl, e.OccurredAt);
        }).ToList();
    }
}
