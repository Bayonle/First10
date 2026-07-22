using First10.Application.Outbound;
using First10.Domain.Abstractions;
using First10.Domain.Channels;
using First10.Domain.Conversations;
using First10.Domain.Incidents;
using First10.Domain.Triage;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace First10.Application.Ingest;

/// <summary>
/// The triage funnel orchestrator (D-008). Order per message:
///   dedup → conversation resolve → active-session routing (skip intent)
///   → Stage 0 gates (rate limit, reputation, pHash, geofence, flood)
///   → Stage 1 intent (text only; evidence-first for photos)
///   → Stage 2 disposition → ticket + timeline + outbound (challenge / canned reply).
/// M2 replaces the stub session with the ReportingSession saga; the funnel itself stays.
/// </summary>
public static class IngestInboundMessageHandler
{
    public static async Task<OutgoingMessages> Handle(
        InboundChannelMessage message,
        First10DbContext db,
        IIntentClassifier intentClassifier,
        IMediaStore mediaStore,
        IPerceptualHasher hasher,
        TriageOptions options,
        ILogger logger,
        CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();
        var now = DateTimeOffset.UtcNow;

        // ---- Dedup (D-005) ----
        var isDuplicate = await db.TimelineEntries.AnyAsync(
            t => t.Channel == message.Channel && t.ExternalMessageId == message.ExternalMessageId, ct);
        if (isDuplicate)
        {
            logger.LogInformation("Duplicate delivery dropped: {Channel}/{ExternalMessageId}",
                message.Channel, message.ExternalMessageId);
            return outgoing;
        }

        // ---- Conversation + reputation ----
        var conversation = await db.Conversations.SingleOrDefaultAsync(
            c => c.Channel == message.Channel && c.ExternalUserId == message.ExternalUserId, ct);
        if (conversation is null)
        {
            conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                Channel = message.Channel,
                ExternalUserId = message.ExternalUserId,
                CreatedAt = now,
            };
            db.Conversations.Add(conversation);
        }
        var previousInboundAt = conversation.LastInboundAt; // default(DateTimeOffset) on first contact
        conversation.LastInboundAt = now;

        var trust = await db.ReporterReputations
            .Where(r => r.Channel == message.Channel && r.ExternalUserId == message.ExternalUserId)
            .Select(r => (TrustLevel?)r.Trust)
            .SingleOrDefaultAsync(ct) ?? TrustLevel.Neutral;

        if (trust == TrustLevel.Blocked)
        {
            logger.LogInformation("Blocked reporter {User} dropped", message.ExternalUserId);
            return outgoing; // record nothing, send nothing
        }

        // ---- Stage 0 media analysis (images) ----
        var imageAnalysis = message.Kind == InboundKind.Image && message.MediaRef is not null
            ? await AnalyzeImage(message.MediaRef, db, mediaStore, hasher, options, now, logger, ct)
            : null;

        var outsideCorridor = message.Location is { } pin
            && !CorridorGeofence.IsNearCorridor(pin, options.CorridorCenterline, options.CorridorBufferKm);

        // ---- Active-session routing: enrich the open ticket, no intent call ----
        IncidentTicket? activeTicket = conversation.ActiveTicketId is { } activeId
            ? await db.Tickets.SingleOrDefaultAsync(t => t.Id == activeId && t.Status == TicketStatus.Provisional, ct)
            : null;

        // Session boundary (lazy until M2's saga): a session ends on EITHER
        //   (a) inactivity — silence longer than the window, or
        //   (b) age — the ticket is older than the max session age (regular messages
        //       must not keep an ancient session alive by resetting the clock).
        var inactivityExceeded = previousInboundAt != default
            && now - previousInboundAt > TimeSpan.FromMinutes(options.SessionInactivityMinutes);
        var maxAgeExceeded = activeTicket is not null
            && now - activeTicket.CreatedAt > TimeSpan.FromMinutes(options.SessionMaxAgeMinutes);

        if (activeTicket is not null && (inactivityExceeded || maxAgeExceeded))
        {
            var challengeUnanswered = activeTicket.ChallengeSentAt is not null
                && activeTicket.Evidence <= EvidenceLevel.TextOnly
                && activeTicket.LocationResolvedAt is null;

            var reason = maxAgeExceeded
                ? $"session older than {options.SessionMaxAgeMinutes} minutes"
                : $"{options.SessionInactivityMinutes}+ minutes of silence";

            if (challengeUnanswered)
            {
                // Nothing actionable ever arrived — expire, but keep it visible:
                // the dispatcher makes the kill call, never a timer (D-007).
                activeTicket.Status = TicketStatus.ExpiredUnverified;
                AppendSystemNote(db, activeTicket.Id, conversation.Id,
                    $"Session expired ({reason}): challenge was never answered");
            }
            else
            {
                // Evidence and/or location exist — the incident stays pending for
                // dispatch; only the reporter session closes.
                AppendSystemNote(db, activeTicket.Id, conversation.Id,
                    $"Reporter session closed ({reason}) — later messages open a new incident");
            }

            activeTicket.UpdatedAt = now;
            outgoing.Add(new TicketUpserted(activeTicket.Id));
            conversation.ActiveTicketId = null;
            activeTicket = null; // current message is triaged as a fresh report below
        }

        if (activeTicket is not null)
        {
            AppendEntry(db, activeTicket.Id, conversation, message, imageAnalysis?.MediaRef);
            await EnrichTicket(activeTicket, conversation, message, imageAnalysis, outsideCorridor, db, options, now, outgoing, ct);
            outgoing.Add(new TicketUpserted(activeTicket.Id));
            if (imageAnalysis is not null) imageAnalysis.Asset.TicketId = activeTicket.Id;
            return outgoing;
        }

        // ---- New-incident attempt: Stage 0 rate limit ----
        var windowStart = now.AddMinutes(-options.RateLimitWindowMinutes);
        var recentOpens = await db.TimelineEntries
            .Where(t => t.ConversationId == conversation.Id
                && t.Direction == TimelineDirection.Inbound
                && t.OccurredAt >= windowStart)
            .Select(t => t.TicketId)
            .Distinct()
            .CountAsync(ct);
        var rateLimited = recentOpens >= options.MaxNewIncidentsPerWindow;

        // ---- Stage 1 intent (evidence-first for non-text) ----
        IntentResult intent = message.Kind switch
        {
            // A photo IS the report — never wait on a classifier (D-008 evidence-first).
            InboundKind.Image => new IntentResult(MessageIntent.NewIncident, "english", IntentConfidence.High, "evidence-first"),
            // No STT until M2: treat a voice note as an incident report, flag for review.
            InboundKind.Voice => new IntentResult(MessageIntent.NewIncident, "english", IntentConfidence.Low, "voice-untranscribed"),
            InboundKind.LocationPin => new IntentResult(MessageIntent.NewIncident, "english", IntentConfidence.Low, "pin-only"),
            _ => await intentClassifier.ClassifyAsync(message.Text ?? string.Empty, ct),
        };

        // ---- Stage 0 flood state (R11) ----
        var floodWindowStart = now.AddMinutes(-options.FloodWindowMinutes);
        var floodActive = await db.Tickets.CountAsync(t => t.CreatedAt >= floodWindowStart, ct)
            >= options.FloodDistinctConversations;

        // ---- Stage 2 disposition ----
        var evidence = message.Kind switch
        {
            InboundKind.Image => EvidenceLevel.Photo,
            InboundKind.Voice => EvidenceLevel.VoiceOnly,
            InboundKind.LocationPin => EvidenceLevel.TextOnly, // pin alone: location but no scene evidence
            _ => EvidenceLevel.TextOnly,
        };

        var decision = DispositionEngine.Decide(new TriageInput(
            intent.Intent, evidence, trust, rateLimited, floodActive,
            imageAnalysis?.Reused ?? false, outsideCorridor));

        switch (decision.Disposition)
        {
            case Disposition.Drop:
                logger.LogInformation("Dropped inbound from {User}: {Flags}",
                    message.ExternalUserId, string.Join(',', decision.Flags));
                return outgoing; // dropped: no ticket, no reply (starve spam)

            case Disposition.None:
                // Not an incident — record on the conversation (no ticket) + canned reply,
                // throttled: at most one canned reply per conversation per 10 minutes
                // (repeated greetings must not produce a parrot).
                AppendEntry(db, ticketId: null, conversation, message, imageAnalysis?.MediaRef);
                if (conversation.LastCannedReplyAt is null || conversation.LastCannedReplyAt < now.AddMinutes(-10))
                {
                    conversation.LastCannedReplyAt = now;
                    outgoing.Add(new SendOutboundMessage(conversation.Id, null, OutboundKind.CannedReply, intent.Language));
                }
                return outgoing;
        }

        // ---- Open the provisional ticket (D-007: at session START) ----
        var ticket = new IncidentTicket
        {
            Id = Guid.NewGuid(),
            Status = TicketStatus.Provisional,
            Summary = Summarize(message),
            Disposition = decision.Disposition,
            Evidence = evidence,
            Language = intent.Language,
            Flags = decision.Flags.Count > 0 ? string.Join(',', decision.Flags) : null,
            ClassifierVersion = intent.ClassifierVersion,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Tickets.Add(ticket);
        conversation.ActiveTicketId = ticket.Id;
        if (imageAnalysis is not null) imageAnalysis.Asset.TicketId = ticket.Id;

        AppendEntry(db, ticket.Id, conversation, message, imageAnalysis?.MediaRef);
        AppendSystemNote(db, ticket.Id, conversation.Id,
            $"Triaged: {decision.Disposition} · evidence={evidence} · intent={intent.Intent}({intent.Confidence}, {intent.ClassifierVersion})"
            + (decision.Flags.Count > 0 ? $" · flags=[{string.Join(',', decision.Flags)}]" : ""));

        if (message.Location is not null)
        {
            ticket.LocationResolvedAt = now;
        }

        // Every report gets an immediate response — a reporter must never wonder
        // whether their message landed (paper §1.2 point 5; cockpit-tested gap).
        if (decision.SendChallenge)
        {
            ticket.ChallengeSentAt = now;
            // Pin-first reports already resolved location — don't ask for what they
            // just sent; the "location received, please send a photo" text is the
            // correct remaining ask (edge case found via browser sweep).
            var challengeKind = ticket.LocationResolvedAt is not null
                ? OutboundKind.PinReceivedAck
                : OutboundKind.ElicitationChallenge;
            outgoing.Add(new SendOutboundMessage(conversation.Id, ticket.Id, challengeKind, intent.Language));
        }
        else if (ticket.Evidence >= EvidenceLevel.VoiceOnly && ticket.LocationResolvedAt is null)
        {
            // Scene evidence first (photo/voice), no location: the §1.4 pin-fallback flow.
            ticket.LocationRequestSentAt = now;
            outgoing.Add(new SendOutboundMessage(conversation.Id, ticket.Id, OutboundKind.LocationPinRequest, intent.Language));
        }
        else if (ticket.LocationResolvedAt is not null)
        {
            ticket.AckSentAt = now;
            outgoing.Add(new SendOutboundMessage(conversation.Id, ticket.Id, OutboundKind.ReportAck, intent.Language));
        }

        outgoing.Add(new TicketUpserted(ticket.Id));
        return outgoing;
    }

    /// <summary>
    /// Evidence arriving on an open ticket can only raise its disposition (D-007/D-008),
    /// and every contribution is acknowledged exactly once (tracked on the ticket):
    /// pin before photo → PinReceivedAck; photo/voice without location → pin request;
    /// evidence + location complete → ReportAck.
    /// </summary>
    private static async Task EnrichTicket(
        IncidentTicket ticket,
        Conversation conversation,
        InboundChannelMessage message,
        ImageAnalysis? imageAnalysis,
        bool outsideCorridor,
        First10DbContext db,
        TriageOptions options,
        DateTimeOffset now,
        OutgoingMessages outgoing,
        CancellationToken ct)
    {
        ticket.UpdatedAt = now;
        var language = ticket.Language ?? "english";

        // ---- Location resolution + reporter feedback ----
        if (message.Kind == InboundKind.LocationPin && ticket.LocationResolvedAt is null)
        {
            ticket.LocationResolvedAt = now;
            AppendSystemNote(db, ticket.Id, conversation.Id, "Location pin received — location resolved");

            if (ticket.Evidence >= EvidenceLevel.VoiceOnly && ticket.AckSentAt is null)
            {
                ticket.AckSentAt = now; // scene evidence + location → report is complete
                outgoing.Add(new SendOutboundMessage(conversation.Id, ticket.Id, OutboundKind.ReportAck, language));
            }
            else if (ticket.AckSentAt is null)
            {
                // Pin first, still no photo: acknowledge, keep the photo ask alive.
                outgoing.Add(new SendOutboundMessage(conversation.Id, ticket.Id, OutboundKind.PinReceivedAck, language));
            }
        }
        else if (message.Kind is InboundKind.Image or InboundKind.Voice)
        {
            if (ticket.LocationResolvedAt is not null)
            {
                if (ticket.AckSentAt is null)
                {
                    ticket.AckSentAt = now;
                    outgoing.Add(new SendOutboundMessage(conversation.Id, ticket.Id, OutboundKind.ReportAck, language));
                }
            }
            else if (ticket.LocationRequestSentAt is null)
            {
                // Evidence in hand, location still missing — focused pin request
                // (even if the original challenge mentioned it; reporters skim).
                ticket.LocationRequestSentAt = now;
                outgoing.Add(new SendOutboundMessage(conversation.Id, ticket.Id, OutboundKind.LocationPinRequest, language));
            }
        }

        var newEvidence = message.Kind switch
        {
            InboundKind.Image when ticket.Evidence >= EvidenceLevel.Photo => EvidenceLevel.PhotoPlus,
            InboundKind.Image => EvidenceLevel.Photo,
            InboundKind.Voice when ticket.Evidence >= EvidenceLevel.Photo => EvidenceLevel.PhotoPlus,
            InboundKind.Voice when ticket.Evidence < EvidenceLevel.VoiceOnly => EvidenceLevel.VoiceOnly,
            InboundKind.LocationPin when ticket.Evidence >= EvidenceLevel.Photo => EvidenceLevel.PhotoPlus,
            _ => ticket.Evidence,
        };
        if (newEvidence < ticket.Evidence) newEvidence = ticket.Evidence;

        var flags = (ticket.Flags?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? []).ToHashSet();
        if (imageAnalysis?.Reused == true) flags.Add("reused-image");
        if (outsideCorridor) flags.Add("outside-corridor");

        if (newEvidence != ticket.Evidence || !SetEquals(flags, ticket.Flags))
        {
            var floodWindowStart = now.AddMinutes(-options.FloodWindowMinutes);
            var floodActive = await db.Tickets.CountAsync(t => t.CreatedAt >= floodWindowStart && t.Id != ticket.Id, ct)
                >= options.FloodDistinctConversations;

            var trust = await db.ReporterReputations
                .Where(r => r.Channel == conversation.Channel && r.ExternalUserId == conversation.ExternalUserId)
                .Select(r => (TrustLevel?)r.Trust)
                .SingleOrDefaultAsync(ct) ?? TrustLevel.Neutral;

            var decision = DispositionEngine.Decide(new TriageInput(
                MessageIntent.NewIncident, newEvidence, trust,
                RateLimited: false, floodActive,
                flags.Contains("reused-image"), flags.Contains("outside-corridor")));

            // Enrichment never lowers a disposition a human may already be acting on.
            var newDisposition = decision.Disposition > ticket.Disposition ? decision.Disposition : ticket.Disposition;
            if (newDisposition != ticket.Disposition || newEvidence != ticket.Evidence)
            {
                AppendSystemNote(db, ticket.Id, conversation.Id,
                    $"Evidence received: {message.Kind} · {ticket.Disposition}→{newDisposition} · evidence={newEvidence}");
            }
            ticket.Disposition = newDisposition;
            ticket.Evidence = newEvidence;
            ticket.Flags = flags.Count > 0 ? string.Join(',', flags.OrderBy(f => f)) : null;
        }

        SendReminderIfSilent(ticket, conversation, message, now, outgoing);
    }

    /// <summary>
    /// "Never silent, never nagging": if this message earned no other reply (a question
    /// mid-flow, a duplicate photo), re-state whatever the session is waiting for —
    /// or the report's status if it's complete. Throttled: quick texts 30s, media 120s.
    /// </summary>
    private static void SendReminderIfSilent(
        IncidentTicket ticket,
        Conversation conversation,
        InboundChannelMessage message,
        DateTimeOffset now,
        OutgoingMessages outgoing)
    {
        if (outgoing.OfType<SendOutboundMessage>().Any())
        {
            return; // this message already got a real reply
        }

        // Throttle from the last thing we said on this ticket, whatever it was.
        var lastOutbound = Max(ticket.LastReminderSentAt, ticket.ChallengeSentAt,
            ticket.LocationRequestSentAt, ticket.AckSentAt, ticket.LocationResolvedAt)
            ?? ticket.CreatedAt;
        var throttle = message.Kind == InboundKind.Text
            ? TimeSpan.FromSeconds(30)   // a typed question deserves a fast answer
            : TimeSpan.FromSeconds(120); // duplicate media can wait

        if (now - lastOutbound < throttle)
        {
            return;
        }

        var kind = ticket switch
        {
            { AckSentAt: not null } => OutboundKind.StatusUnderReview,
            { LocationResolvedAt: null, Evidence: >= EvidenceLevel.VoiceOnly } => OutboundKind.LocationPinRequest,
            { LocationResolvedAt: not null } => OutboundKind.PinReceivedAck, // photo still pending
            _ => OutboundKind.ElicitationChallenge, // nothing yet — repeat the full ask
        };

        ticket.LastReminderSentAt = now;
        outgoing.Add(new SendOutboundMessage(conversation.Id, ticket.Id, kind, ticket.Language ?? "english"));
    }

    private static DateTimeOffset? Max(params DateTimeOffset?[] values) =>
        values.Where(v => v.HasValue).Max();

    private sealed record ImageAnalysis(string MediaRef, MediaAsset Asset, bool Reused);

    private static async Task<ImageAnalysis?> AnalyzeImage(
        string mediaRef,
        First10DbContext db,
        IMediaStore mediaStore,
        IPerceptualHasher hasher,
        TriageOptions options,
        DateTimeOffset now,
        ILogger logger,
        CancellationToken ct)
    {
        await using var stream = await mediaStore.OpenReadAsync(mediaRef, ct);
        if (stream is null)
        {
            logger.LogWarning("Media {MediaRef} not found in store; skipping pHash", mediaRef);
            return null;
        }

        ulong hash;
        try
        {
            hash = await hasher.HashAsync(stream, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "pHash failed for {MediaRef}; treating as un-hashed", mediaRef);
            return null;
        }

        // Pilot scale: linear scan over the corpus is fine (hundreds of rows).
        var knownHashes = await db.MediaAssets.Select(a => a.PerceptualHash).ToListAsync(ct);
        var reused = knownHashes.Any(known =>
            PerceptualHash.HammingDistance(unchecked((ulong)known), hash) <= options.PerceptualHashThreshold);

        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            MediaRef = mediaRef,
            PerceptualHash = unchecked((long)hash),
            CreatedAt = now,
        };
        db.MediaAssets.Add(asset);

        return new ImageAnalysis(mediaRef, asset, reused);
    }

    private static void AppendEntry(
        First10DbContext db, Guid? ticketId, Conversation conversation,
        InboundChannelMessage message, string? mediaRef)
    {
        db.TimelineEntries.Add(new TimelineEntry
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            ConversationId = conversation.Id,
            Direction = TimelineDirection.Inbound,
            Kind = message.Kind switch
            {
                InboundKind.Image => TimelineEntryKind.Image,
                InboundKind.Voice => TimelineEntryKind.Voice,
                InboundKind.LocationPin => TimelineEntryKind.LocationPin,
                _ => TimelineEntryKind.Text,
            },
            // Truncate defensively: the column caps at 8192 chars, and an oversized
            // insert dead-letters the whole message — a silently lost crash report
            // (found via edge-case sweep: 10k-char text vanished with a 202).
            Text = message.Kind == InboundKind.LocationPin && message.Location is { } pin
                ? $"{pin.Latitude:F5}, {pin.Longitude:F5}"
                : message.Text is { Length: > 8192 } longText ? longText[..8192] : message.Text,
            MediaRef = mediaRef ?? message.MediaRef,
            Channel = message.Channel,
            ExternalMessageId = message.ExternalMessageId,
            OccurredAt = message.OccurredAt,
        });
    }

    private static void AppendSystemNote(First10DbContext db, Guid ticketId, Guid conversationId, string text)
    {
        db.TimelineEntries.Add(new TimelineEntry
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            ConversationId = conversationId,
            Direction = TimelineDirection.System,
            Kind = TimelineEntryKind.StatusChange,
            Text = text,
            OccurredAt = DateTimeOffset.UtcNow,
        });
    }

    private static bool SetEquals(HashSet<string> flags, string? stored) =>
        flags.SetEquals(stored?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? []);

    private static string Summarize(InboundChannelMessage message) =>
        message.Kind switch
        {
            InboundKind.Text when !string.IsNullOrWhiteSpace(message.Text) =>
                message.Text.Length <= 140 ? message.Text : message.Text[..140],
            InboundKind.Image => "[photo received]",
            InboundKind.Voice => "[voice note received]",
            InboundKind.LocationPin => "[location pin received]",
            _ => "[message received]",
        };
}
