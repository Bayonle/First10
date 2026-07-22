using First10.Application.Outbound;
using First10.Application.Sessions;
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
/// The triage funnel orchestrator (D-008) + session routing (M2). Order per message:
///   dedup → conversation resolve → session boundary (lazy backstop; saga is primary)
///   → active-session enrichment OR Stage 0/1/2 triage → ticket + corroboration dedup
///   → feedback outbound → saga + extraction cascades.
/// </summary>
public static class IngestInboundMessageHandler
{
    public static async Task<OutgoingMessages> Handle(
        InboundChannelMessage message,
        First10DbContext db,
        IIntentClassifier intentClassifier,
        ITranscriber transcriber,
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
            return outgoing;
        }

        // ---- Stage 0 media analysis ----
        var imageAnalysis = message.Kind == InboundKind.Image && message.MediaRef is not null
            ? await AnalyzeImage(message.MediaRef, db, mediaStore, hasher, options, now, logger, ct)
            : null;

        // ---- STT (M2, D-010): transcribe voice before triage ----
        string? transcript = null;
        if (message.Kind == InboundKind.Voice && message.MediaRef is not null)
        {
            transcript = await Transcribe(message.MediaRef, mediaStore, transcriber, logger, ct);
        }

        var outsideCorridor = message.Location is { } pin
            && !CorridorGeofence.IsNearCorridor(pin, options.CorridorCenterline, options.CorridorBufferKm);

        // ---- Active-session routing ----
        IncidentTicket? activeTicket = conversation.ActiveTicketId is { } activeId
            ? await db.Tickets.SingleOrDefaultAsync(
                t => t.Id == activeId && (t.Status == TicketStatus.Provisional || t.Status == TicketStatus.Promoted), ct)
            : null;

        // Lazy session boundary — backstop behind the saga's proactive timers.
        var inactivityExceeded = previousInboundAt != default
            && now - previousInboundAt > TimeSpan.FromMinutes(options.SessionInactivityMinutes);
        var maxAgeExceeded = activeTicket is not null
            && now - activeTicket.CreatedAt > TimeSpan.FromMinutes(options.SessionMaxAgeMinutes);

        if (activeTicket is not null && (inactivityExceeded || maxAgeExceeded))
        {
            CloseSession(activeTicket, conversation, maxAgeExceeded, options, db, outgoing);
            activeTicket = null;
        }

        if (activeTicket is not null)
        {
            AppendEntry(db, activeTicket.Id, conversation, message, imageAnalysis?.MediaRef, transcript);
            await EnrichTicket(activeTicket, conversation, message, imageAnalysis, outsideCorridor,
                transcript, db, options, now, outgoing, ct);
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

        // ---- Stage 1 intent (evidence-first for media; transcript when available) ----
        IntentResult intent = message.Kind switch
        {
            InboundKind.Image => new IntentResult(MessageIntent.NewIncident, "english", IntentConfidence.High, "evidence-first"),
            InboundKind.Voice when transcript is not null =>
                (await intentClassifier.ClassifyAsync(transcript, ct)) with { ClassifierVersion = "stt+" },
            InboundKind.Voice => new IntentResult(MessageIntent.NewIncident, "english", IntentConfidence.Low, "voice-untranscribed"),
            InboundKind.LocationPin => new IntentResult(MessageIntent.NewIncident, "english", IntentConfidence.Low, "pin-only"),
            _ => await intentClassifier.ClassifyAsync(message.Text ?? string.Empty, ct),
        };

        // A voice note is physical presence — whatever the transcript classifies as.
        // STT can mangle vernacular into nonsense (a Yoruba report transcribed as
        // gibberish English would classify as spam); the dispatcher's ear decides,
        // never the classifier. Voice ALWAYS triages as an incident, low confidence.
        if (message.Kind == InboundKind.Voice && intent.Intent != MessageIntent.NewIncident)
        {
            intent = intent with { Intent = MessageIntent.NewIncident, Confidence = IntentConfidence.Low };
        }

        // ---- Stage 0 flood state (R11) ----
        var floodWindowStart = now.AddMinutes(-options.FloodWindowMinutes);
        var floodActive = await db.Tickets.CountAsync(t => t.CreatedAt >= floodWindowStart, ct)
            >= options.FloodDistinctConversations;

        // ---- Stage 2 disposition ----
        var evidence = message.Kind switch
        {
            InboundKind.Image => EvidenceLevel.Photo,
            InboundKind.Voice => EvidenceLevel.VoiceOnly,
            InboundKind.LocationPin => EvidenceLevel.TextOnly,
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
                return outgoing;

            case Disposition.None:
                AppendEntry(db, ticketId: null, conversation, message, imageAnalysis?.MediaRef, transcript);
                if (conversation.LastCannedReplyAt is null || conversation.LastCannedReplyAt < now.AddMinutes(-10))
                {
                    conversation.LastCannedReplyAt = now;
                    outgoing.Add(new SendOutboundMessage(conversation.Id, null, OutboundKind.CannedReply, intent.Language));
                }
                return outgoing;
        }

        // ---- Corroboration dedup (M2, paper §1.4): pin-bearing first message may
        // belong to an incident another reporter already opened ----
        if (message.Location is { } location)
        {
            var existing = await FindNearbyOpenIncident(db, conversation.Id, location, options, now, ct);
            if (existing is not null)
            {
                AttachReporter(existing, conversation, message, imageAnalysis, transcript, db, outgoing, now, options);
                return outgoing;
            }
        }

        // ---- Open the provisional ticket (D-007) ----
        var ticket = new IncidentTicket
        {
            Id = Guid.NewGuid(),
            Status = TicketStatus.Provisional,
            Summary = Summarize(message, transcript),
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

        AppendEntry(db, ticket.Id, conversation, message, imageAnalysis?.MediaRef, transcript);
        AppendSystemNote(db, ticket.Id, conversation.Id,
            $"Triaged: {decision.Disposition} · evidence={evidence} · intent={intent.Intent}({intent.Confidence}, {intent.ClassifierVersion})"
            + (decision.Flags.Count > 0 ? $" · flags=[{string.Join(',', decision.Flags)}]" : ""));

        if (message.Location is { } newPin)
        {
            ticket.LocationResolvedAt = now;
            ticket.LocationLat = newPin.Latitude;
            ticket.LocationLng = newPin.Longitude;
        }

        // Every report gets an immediate response (paper §1.2.5).
        if (decision.SendChallenge)
        {
            ticket.ChallengeSentAt = now;
            var challengeKind = ticket.LocationResolvedAt is not null
                ? OutboundKind.PinReceivedAck
                : OutboundKind.ElicitationChallenge;
            outgoing.Add(new SendOutboundMessage(conversation.Id, ticket.Id, challengeKind, intent.Language));
        }
        else if (ticket.Evidence >= EvidenceLevel.VoiceOnly && ticket.LocationResolvedAt is null)
        {
            ticket.LocationRequestSentAt = now;
            outgoing.Add(new SendOutboundMessage(conversation.Id, ticket.Id, OutboundKind.LocationPinRequest, intent.Language));
        }
        else if (ticket.LocationResolvedAt is not null)
        {
            ticket.AckSentAt = now;
            outgoing.Add(new SendOutboundMessage(conversation.Id, ticket.Id, OutboundKind.ReportAck, intent.Language));
        }

        MaybePromote(ticket, db, conversation.Id);

        // ---- M2 cascades: session saga (timers) + async extraction ----
        outgoing.Add(new SessionOpened(
            ticket.Id,
            PinAskPending: ticket.LocationResolvedAt is null,
            ChallengePending: ticket.ChallengeSentAt is not null));
        outgoing.Add(new RunExtraction(ticket.Id, conversation.Id));

        outgoing.Add(new TicketUpserted(ticket.Id));
        return outgoing;
    }

    /// <summary>Evidence arriving on an open ticket: acks, evidence raise, merge check, promotion.</summary>
    private static async Task EnrichTicket(
        IncidentTicket ticket,
        Conversation conversation,
        InboundChannelMessage message,
        ImageAnalysis? imageAnalysis,
        bool outsideCorridor,
        string? transcript,
        First10DbContext db,
        TriageOptions options,
        DateTimeOffset now,
        OutgoingMessages outgoing,
        CancellationToken ct)
    {
        ticket.UpdatedAt = now;
        var language = ticket.Language ?? "english";

        // ---- Pin correction: reporters fat-finger pins ("sorry wrong pin") — a later
        // pin REPLACES the location and re-evaluates the corridor flag (found live:
        // an Abuja mis-pin stuck forever, correction silently discarded) ----
        if (message.Kind == InboundKind.LocationPin && ticket.LocationResolvedAt is not null && message.Location is { } correctedPin)
        {
            ticket.LocationLat = correctedPin.Latitude;
            ticket.LocationLng = correctedPin.Longitude;
            var nowOutside = !CorridorGeofence.IsNearCorridor(
                correctedPin, options.CorridorCenterline, options.CorridorBufferKm);
            var correctionFlags = (ticket.Flags?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? []).ToHashSet();
            var flagChanged = nowOutside ? correctionFlags.Add("outside-corridor") : correctionFlags.Remove("outside-corridor");
            if (flagChanged)
            {
                ticket.Flags = correctionFlags.Count > 0 ? string.Join(',', correctionFlags.OrderBy(f => f)) : null;
            }
            AppendSystemNote(db, ticket.Id, conversation.Id,
                $"Location updated by reporter → ({correctedPin.Latitude:F5}, {correctedPin.Longitude:F5})"
                + (nowOutside ? " [outside corridor]" : ""));
        }

        // ---- Location resolution + reporter feedback ----
        if (message.Kind == InboundKind.LocationPin && ticket.LocationResolvedAt is null && message.Location is { } pin)
        {
            ticket.LocationResolvedAt = now;
            ticket.LocationLat = pin.Latitude;
            ticket.LocationLng = pin.Longitude;
            AppendSystemNote(db, ticket.Id, conversation.Id, "Location pin received — location resolved");

            // The pin may reveal this is the SAME incident another reporter already
            // opened — merge into the older ticket (M2 corroboration).
            var older = await FindNearbyOpenIncident(db, conversation.Id, pin, options, now, ct);
            if (older is not null)
            {
                await MergeInto(older, ticket, conversation, db, outgoing, now, options, ct);
                return;
            }

            if (ticket.Evidence >= EvidenceLevel.VoiceOnly && ticket.AckSentAt is null)
            {
                ticket.AckSentAt = now;
                outgoing.Add(new SendOutboundMessage(conversation.Id, ticket.Id, OutboundKind.ReportAck, language));
            }
            else if (ticket.AckSentAt is null)
            {
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

        MaybePromote(ticket, db, conversation.Id);

        if (message.Kind is InboundKind.Text or InboundKind.Voice or InboundKind.Image)
        {
            outgoing.Add(new RunExtraction(ticket.Id, conversation.Id)); // narrative changed
        }

        SendReminderIfSilent(ticket, conversation, message, now, outgoing);
    }

    // ---- M2 corroboration (paper §1.4: 200m + 5min ⇒ auto-verify) ----

    private static async Task<IncidentTicket?> FindNearbyOpenIncident(
        First10DbContext db, Guid ownConversationId, GeoPoint location,
        TriageOptions options, DateTimeOffset now, CancellationToken ct)
    {
        var windowStart = now.AddMinutes(-options.DedupWindowMinutes);
        var candidates = await db.Tickets
            .Where(t => (t.Status == TicketStatus.Provisional || t.Status == TicketStatus.Promoted)
                && t.LocationLat != null && t.UpdatedAt >= windowStart)
            .ToListAsync(ct);

        // Must be a DIFFERENT reporter's incident (corroboration means independence).
        // Checked against timeline history, not ActiveTicketId — a closed session must
        // not let a reporter corroborate their own earlier report.
        foreach (var candidate in candidates.OrderBy(t => t.CreatedAt))
        {
            var isOwn = await db.TimelineEntries.AnyAsync(
                e => e.TicketId == candidate.Id && e.ConversationId == ownConversationId, ct);
            if (isOwn) continue;

            var distanceKm = CorridorGeofence.DistanceKm(
                location, new GeoPoint(candidate.LocationLat!.Value, candidate.LocationLng!.Value));
            if (distanceKm * 1000 <= options.DedupRadiusMeters)
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>New reporter's first message lands directly on an existing incident.</summary>
    private static void AttachReporter(
        IncidentTicket existing, Conversation conversation, InboundChannelMessage message,
        ImageAnalysis? imageAnalysis, string? transcript, First10DbContext db,
        OutgoingMessages outgoing, DateTimeOffset now, TriageOptions options)
    {
        conversation.ActiveTicketId = existing.Id;
        if (imageAnalysis is not null) imageAnalysis.Asset.TicketId = existing.Id;
        AppendEntry(db, existing.Id, conversation, message, imageAnalysis?.MediaRef, transcript);
        Corroborate(existing, db, conversation.Id, now, options);
        outgoing.Add(new SendOutboundMessage(conversation.Id, existing.Id, OutboundKind.ReportAck, existing.Language ?? "english"));
        outgoing.Add(new RunExtraction(existing.Id, conversation.Id));
        outgoing.Add(new TicketUpserted(existing.Id));
    }

    /// <summary>A pin on an open ticket revealed it's the same incident as an older one.</summary>
    private static async Task MergeInto(
        IncidentTicket survivor, IncidentTicket merged, Conversation conversation,
        First10DbContext db, OutgoingMessages outgoing, DateTimeOffset now,
        TriageOptions options, CancellationToken ct)
    {
        // Re-point the merged ticket's timeline onto the survivor — the relay timeline
        // keeps per-reporter identity via ConversationId (paper §1.4 relay).
        var entries = await db.TimelineEntries.Where(e => e.TicketId == merged.Id).ToListAsync(ct);
        foreach (var entry in entries)
        {
            entry.TicketId = survivor.Id;
        }

        merged.Status = TicketStatus.Merged;
        merged.UpdatedAt = now;
        conversation.ActiveTicketId = survivor.Id;

        Corroborate(survivor, db, conversation.Id, now, options);
        // The merged reporter's location contribution counts for the survivor too.
        if (survivor.LocationResolvedAt is null && merged.LocationResolvedAt is not null)
        {
            survivor.LocationResolvedAt = merged.LocationResolvedAt;
            survivor.LocationLat = merged.LocationLat;
            survivor.LocationLng = merged.LocationLng;
        }

        outgoing.Add(new SendOutboundMessage(conversation.Id, survivor.Id, OutboundKind.ReportAck, survivor.Language ?? "english"));
        outgoing.Add(new SessionEnded(merged.Id)); // complete the merged ticket's saga
        outgoing.Add(new RunExtraction(survivor.Id, conversation.Id));
        outgoing.Add(new TicketUpserted(survivor.Id));
        outgoing.Add(new TicketUpserted(merged.Id));
    }

    private static void Corroborate(
        IncidentTicket ticket, First10DbContext db, Guid conversationId, DateTimeOffset now, TriageOptions options)
    {
        ticket.ReporterCount += 1;
        ticket.Disposition = Disposition.AutoVerify;
        var flags = (ticket.Flags?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? []).ToHashSet();
        flags.Add("corroborated");
        ticket.Flags = string.Join(',', flags.OrderBy(f => f));
        ticket.UpdatedAt = now;
        AppendSystemNote(db, ticket.Id, conversationId,
            $"Reporter #{ticket.ReporterCount} corroborated within {options.DedupRadiusMeters}m/{options.DedupWindowMinutes}min — AUTO-VERIFIED");
        MaybePromote(ticket, db, conversationId);
    }

    /// <summary>Promotion rule (D-007): (photo OR corroboration) AND location resolved.</summary>
    private static void MaybePromote(IncidentTicket ticket, First10DbContext db, Guid conversationId)
    {
        if (ticket.Status == TicketStatus.Provisional
            && ticket.LocationResolvedAt is not null
            && (ticket.Evidence >= EvidenceLevel.Photo || ticket.ReporterCount >= 2))
        {
            ticket.Status = TicketStatus.Promoted;
            AppendSystemNote(db, ticket.Id, conversationId,
                "PROMOTED: evidence sufficiency met — (photo OR corroboration) AND location");
        }
    }

    private static void CloseSession(
        IncidentTicket activeTicket, Conversation conversation, bool maxAgeExceeded,
        TriageOptions options, First10DbContext db, OutgoingMessages outgoing)
    {
        var now = DateTimeOffset.UtcNow;
        var reason = maxAgeExceeded
            ? $"session older than {options.SessionMaxAgeMinutes} minutes"
            : $"{options.SessionInactivityMinutes}+ minutes of silence";

        var challengeUnanswered = activeTicket.ChallengeSentAt is not null
            && activeTicket.Evidence <= EvidenceLevel.TextOnly
            && activeTicket.LocationResolvedAt is null;

        if (challengeUnanswered)
        {
            activeTicket.Status = TicketStatus.ExpiredUnverified;
            AppendSystemNote(db, activeTicket.Id, conversation.Id,
                $"Session expired ({reason}): challenge was never answered");
        }
        else
        {
            AppendSystemNote(db, activeTicket.Id, conversation.Id,
                $"Reporter session closed ({reason}) — later messages open a new incident");
        }

        activeTicket.UpdatedAt = now;
        outgoing.Add(new TicketUpserted(activeTicket.Id));
        outgoing.Add(new SessionEnded(activeTicket.Id));
        conversation.ActiveTicketId = null;
    }

    /// <summary>
    /// "Never silent, never nagging": if this message earned no other reply, re-state
    /// whatever the session is waiting for. Throttled: texts 30s, media 120s.
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
            return;
        }

        var lastOutbound = Max(ticket.LastReminderSentAt, ticket.ChallengeSentAt,
            ticket.LocationRequestSentAt, ticket.AckSentAt, ticket.LocationResolvedAt)
            ?? ticket.CreatedAt;
        var throttle = message.Kind == InboundKind.Text
            ? TimeSpan.FromSeconds(30)
            : TimeSpan.FromSeconds(120);

        if (now - lastOutbound < throttle)
        {
            return;
        }

        var kind = ticket switch
        {
            { AckSentAt: not null } => OutboundKind.StatusUnderReview,
            { LocationResolvedAt: null, Evidence: >= EvidenceLevel.VoiceOnly } => OutboundKind.LocationPinRequest,
            { LocationResolvedAt: not null } => OutboundKind.PinReceivedAck,
            _ => OutboundKind.ElicitationChallenge,
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

        if (PerceptualHash.IsDegenerate(hash))
        {
            logger.LogInformation("Degenerate pHash for {MediaRef} — skipping reuse detection", mediaRef);
            return null;
        }

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

    private static async Task<string?> Transcribe(
        string mediaRef, IMediaStore mediaStore, ITranscriber transcriber, ILogger logger, CancellationToken ct)
    {
        await using var audio = await mediaStore.OpenReadAsync(mediaRef, ct);
        if (audio is null) return null;
        try
        {
            return await transcriber.TranscribeAsync(audio, mediaStore.GetContentType(mediaRef), ct);
        }
        catch (Exception ex)
        {
            // STT is enrichment, not a gate — the voice note still triages (low confidence).
            logger.LogWarning(ex, "Transcription failed for {MediaRef}", mediaRef);
            return null;
        }
    }

    private static void AppendEntry(
        First10DbContext db, Guid? ticketId, Conversation conversation,
        InboundChannelMessage message, string? mediaRef, string? transcript)
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
            Text = message.Kind == InboundKind.LocationPin && message.Location is { } pin
                ? $"{pin.Latitude:F5}, {pin.Longitude:F5}"
                : message.Text is { Length: > 8192 } longText ? longText[..8192] : message.Text,
            MediaRef = mediaRef ?? message.MediaRef,
            TranscriptText = transcript is { Length: > 8192 } longTranscript ? longTranscript[..8192] : transcript,
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

    private static string Summarize(InboundChannelMessage message, string? transcript)
    {
        var text = message.Kind == InboundKind.Voice ? transcript : message.Text;
        return message.Kind switch
        {
            InboundKind.Voice when text is not null => text.Length <= 140 ? $"🎙 {text}" : $"🎙 {text[..140]}",
            InboundKind.Voice => "[voice note received]",
            InboundKind.Text when !string.IsNullOrWhiteSpace(text) => text!.Length <= 140 ? text : text[..140],
            InboundKind.Image => "[photo received]",
            InboundKind.LocationPin => "[location pin received]",
            _ => "[message received]",
        };
    }
}
