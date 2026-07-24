using First10.Application.Outbound;
using First10.Domain.Abstractions;
using First10.Domain.Incidents;
using First10.Domain.Triage;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace First10.Application.Ingest;

/// <summary>Cascaded by ingest whenever ticket content changed — runs off the hot path.</summary>
public sealed record RunExtraction(Guid TicketId, Guid ConversationId);

/// <summary>
/// Async AI extraction (D-014): builds the narrative from the ticket's timeline
/// (texts + voice transcripts) plus the latest blurred photo, extracts structured
/// fields, and — first time only — sends the clinically-gated micro-instruction.
/// Runs behind the durable queue so ingest latency stays flat.
/// </summary>
public static class RunExtractionHandler
{
    public static async Task<OutgoingMessages> Handle(
        RunExtraction command,
        First10DbContext db,
        IIncidentExtractor extractor,
        IMediaStore mediaStore,
        TriageOptions options,
        ILogger logger,
        CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == command.TicketId, ct);
        if (ticket is null || ticket.Status is TicketStatus.Merged or TicketStatus.Rejected)
        {
            return outgoing;
        }

        var entries = await db.TimelineEntries
            .Where(e => e.TicketId == ticket.Id && e.Direction == TimelineDirection.Inbound)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(ct);

        var narrativeParts = entries
            .Where(e => e.Kind != TimelineEntryKind.LocationPin) // coordinates aren't narrative
            .Select(e => e.Kind == TimelineEntryKind.Voice ? e.TranscriptText : e.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        var narrative = narrativeParts.Count > 0 ? string.Join("\n", narrativeParts) : null;

        var latestImageRef = entries.LastOrDefault(e => e.Kind == TimelineEntryKind.Image)?.MediaRef;
        Stream? image = latestImageRef is not null
            ? await mediaStore.OpenReadAsync(latestImageRef, ct)
            : null;

        ExtractionResult result;
        await using (image)
        {
            try
            {
                result = await extractor.ExtractAsync(
                    new ExtractionInput(narrative, image, ticket.Language ?? "english"), ct);
            }
            catch (Exception ex)
            {
                // Extraction is enrichment, never a gate: the ticket is already in the
                // queue; a failed extraction must not block or retry-storm.
                logger.LogWarning(ex, "Extraction failed for ticket {TicketId}; leaving raw fields", ticket.Id);
                return outgoing;
            }
        }

        // ---- Landmark-inferred location: the corridor's natural addressing ("accident
        // for Kara bridge"). Approximate by construction — a real pin ALWAYS outranks
        // it (never overwrite Pin), it never triggers corroboration merges, and the
        // map renders it visibly approximate. It DOES count as located: FRSC can roll
        // on a landmark, and a pinless reporter shouldn't be nagged forever.
        if (result.LandmarkKey is not null
            && ticket.LocationSource != LocationSource.Pin
            && ticket.LocationLandmark != result.LandmarkKey
            && First10.Domain.Triage.CorridorLandmarks.ByKey(result.LandmarkKey) is { } landmark)
        {
            ticket.LocationLat = landmark.Lat;
            ticket.LocationLng = landmark.Lng;
            ticket.LocationSource = LocationSource.LandmarkInferred;
            ticket.LocationLandmark = landmark.Key;
            ticket.LocationResolvedAt ??= DateTimeOffset.UtcNow;
            db.TimelineEntries.Add(new TimelineEntry
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                ConversationId = command.ConversationId,
                Direction = TimelineDirection.System,
                Kind = TimelineEntryKind.StatusChange,
                Text = $"Location inferred from landmark: ≈ {landmark.Name} (±{landmark.RadiusKm:0.#}km — a pin will refine it)",
                OccurredAt = DateTimeOffset.UtcNow,
            });
            IngestInboundMessageHandler.MaybePromote(ticket, db, command.ConversationId);
        }

        ticket.Severity = result.Severity;
        ticket.CasualtyEstimate = result.CasualtyEstimate;
        ticket.ExtractorVersion = result.ExtractorVersion;
        ticket.Summary = result.DispatcherSummary.Length <= 2048
            ? result.DispatcherSummary
            : result.DispatcherSummary[..2048];
        ticket.UpdatedAt = DateTimeOffset.UtcNow;

        // Cross-modal consistency (D-008): a photo that contradicts the narrative caps
        // an uncorroborated ticket at Review — flagged, visible, never dropped.
        // Guard on an image actually existing: models return false on no-photo inputs
        // (found live: every text-only ticket got flagged).
        if (!result.PhotoMatchesNarrative && latestImageRef is not null)
        {
            var flags = (ticket.Flags?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? []).ToHashSet();
            if (flags.Add("photo-mismatch"))
            {
                ticket.Flags = string.Join(',', flags.OrderBy(f => f));
                if (ticket.Disposition > First10.Domain.Triage.Disposition.Review && ticket.ReporterCount == 1)
                {
                    db.TimelineEntries.Add(new TimelineEntry
                    {
                        Id = Guid.NewGuid(),
                        TicketId = ticket.Id,
                        ConversationId = command.ConversationId,
                        Direction = TimelineDirection.System,
                        Kind = TimelineEntryKind.StatusChange,
                        Text = $"Photo does not match narrative — {ticket.Disposition}→Review",
                        OccurredAt = DateTimeOffset.UtcNow,
                    });
                    ticket.Disposition = First10.Domain.Triage.Disposition.Review;
                }
            }
        }

        // ---- Micro-instruction: once per ticket, clinical gate enforced (paper §1.4) ----
        if (ticket.InstructionSentAt is null && ticket.Disposition > Disposition.Drop)
        {
            var language = ticket.Language ?? "english";
            var template = await db.MicroInstructionTemplates
                .Where(t => t.Key == result.TemplateKey && t.Language == language)
                .OrderByDescending(t => t.Version)
                .FirstOrDefaultAsync(ct)
                ?? await db.MicroInstructionTemplates // language fallback: english
                    .Where(t => t.Key == result.TemplateKey && t.Language == "english")
                    .OrderByDescending(t => t.Version)
                    .FirstOrDefaultAsync(ct);

            if (template is null)
            {
                logger.LogWarning("No micro-instruction template for key {Key}", result.TemplateKey);
            }
            else if (template.ApprovedAt is null && !options.AllowUnapprovedTemplates)
            {
                // G3 gate, structurally: unapproved clinical content never ships in pilot config.
                logger.LogWarning(
                    "Template {Key}/{Language} lacks clinical approval — instruction NOT sent (G3 gate)",
                    template.Key, template.Language);
            }
            else
            {
                ticket.InstructionSentAt = DateTimeOffset.UtcNow;
                var latency = (ticket.InstructionSentAt.Value - ticket.CreatedAt).TotalSeconds;
                db.TimelineEntries.Add(new TimelineEntry
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticket.Id,
                    ConversationId = command.ConversationId,
                    Direction = TimelineDirection.System,
                    Kind = TimelineEntryKind.StatusChange,
                    Text = $"Micro-instruction '{template.Key}' sent ({latency:F0}s after first message)"
                        + (template.ApprovedAt is null ? " [UNAPPROVED — dev only]" : ""),
                    OccurredAt = DateTimeOffset.UtcNow,
                });
                outgoing.Add(new SendOutboundMessage(
                    command.ConversationId, ticket.Id, OutboundKind.MicroInstruction, language, template.Id));
            }
        }

        // Timeline digest + contradiction surfacing (R1f): worth an AI pass once there
        // is disagreement potential — multiple reporters, or narrative + location both.
        if (ticket.ReporterCount >= 2 || (ticket.LocationResolvedAt is not null && narrative is not null))
        {
            outgoing.Add(new First10.Application.Summaries.RunSummarization(ticket.Id, command.ConversationId));
        }

        outgoing.Add(new TicketUpserted(ticket.Id));
        return outgoing;
    }
}
