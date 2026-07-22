namespace First10.Application.Outbound;

public enum OutboundKind
{
    /// <summary>Elicitation: "send a photo and your location pin" (D-008 CHALLENGE).</summary>
    ElicitationChallenge = 0,
    /// <summary>Non-incident reply (greeting/question) — "this line is for crash reports".</summary>
    CannedReply = 1,
    /// <summary>Pin received before scene evidence — acknowledge, still ask for a photo.</summary>
    PinReceivedAck = 2,
    /// <summary>Scene evidence present, location missing — the paper's §1.4 pin-fallback request.</summary>
    LocationPinRequest = 3,
    /// <summary>Evidence + location both in: "report complete, with FRSC dispatch for review".
    /// Deliberately promises review, never dispatch — dispatch messages only ever follow
    /// an explicit dispatcher action (R1e).</summary>
    ReportAck = 4,
    /// <summary>Reply to "what's happening?" on a complete report. Review-status only —
    /// dispatch updates come exclusively from dispatcher actions (R1e).</summary>
    StatusUnderReview = 5,
    /// <summary>Clinically pre-approved safety template (paper §1.4). Text resolved from
    /// the template store by TemplateId — never from static strings, never generated.</summary>
    MicroInstruction = 6,

    // Loop-closure statuses (paper §1.4). Sent EXCLUSIVELY by DispatcherActionHandler —
    // no other code path may emit these (R1e). Transport wording carries status only:
    // no victim identity, no medical detail (paper §1.5 exclusions).
    DispatchedNotice = 7,
    ArrivedNotice = 8,
    TransportedNotice = 9,
    /// <summary>Late reporter of an incident already being handled (paper D-007 decoupling).</summary>
    IncidentAlreadyHandled = 10,
}

/// <summary>
/// Channel-agnostic outbound command (D-005). Per-channel Wolverine handlers translate;
/// M1 ships the Local sender, WhatsApp lands in M5. Cascaded from ingest through the
/// outbox, so a send can only happen if the triage transaction committed.
/// </summary>
public sealed record SendOutboundMessage(
    Guid ConversationId,
    Guid? TicketId,
    OutboundKind Kind,
    string Language,
    Guid? TemplateId = null);

public static class OutboundTexts
{
    public static string For(OutboundKind kind, string language) => (kind, language) switch
    {
        (OutboundKind.ElicitationChallenge, "pidgin") =>
            "Abeg snap photo of the accident and share your location pin make help fit find the place quick.",
        (OutboundKind.ElicitationChallenge, "yoruba") =>
            "Jọwọ ya fọto ibi ìjàǹbá náà kí o sì fi location pin ranṣẹ́ kí ìrànlọ́wọ́ lè dé kíákíá.",
        (OutboundKind.ElicitationChallenge, _) =>
            "Please send a photo of the scene and share your location pin so help can find the place quickly.",

        (OutboundKind.CannedReply, "pidgin") =>
            "This line na for road accident report for Lagos-Ibadan expressway (Berger to Mowe). If you dey see accident now, send photo or voice note.",
        (OutboundKind.CannedReply, "yoruba") =>
            "Ila yìí jẹ́ fún ìròyìn ìjàǹbá ojú-ọ̀nà ní Lagos-Ibadan expressway (Berger dé Mowe). Bí o bá rí ìjàǹbá, fi fọto tàbí ohùn ranṣẹ́.",
        (OutboundKind.CannedReply, _) =>
            "This line is for road crash reports on the Lagos-Ibadan expressway (Berger to Mowe). If you are witnessing a crash, send a photo or voice note now.",

        (OutboundKind.PinReceivedAck, "pidgin") =>
            "We don see your location, thank you. If e safe, abeg still snap photo of the scene send am.",
        (OutboundKind.PinReceivedAck, "yoruba") =>
            "A ti rí location yín, ẹ ṣé. Bí ó bá ṣe àìléwu, ẹ jọwọ tún ya fọto ibi ìṣẹ̀lẹ̀ náà ránṣẹ́.",
        (OutboundKind.PinReceivedAck, _) =>
            "Location received, thank you. If you can do so safely, please also send a photo of the scene.",

        (OutboundKind.LocationPinRequest, "pidgin") =>
            "Thank you. Abeg share your location pin make responders fit locate the place sharp sharp.",
        (OutboundKind.LocationPinRequest, "yoruba") =>
            "Ẹ ṣé. Ẹ jọwọ fi location pin yín ránṣẹ́ kí àwọn olùrànlọ́wọ́ lè rí ibẹ̀ kíákíá.",
        (OutboundKind.LocationPinRequest, _) =>
            "Thank you. Please share your location pin so responders can find the place quickly.",

        (OutboundKind.ReportAck, "pidgin") =>
            "We don receive your full report. E dey with FRSC dispatch now for review. We go update you.",
        (OutboundKind.ReportAck, "yoruba") =>
            "A ti gba ìjábọ̀ yín pátápátá. Ó ti wà lọ́wọ́ FRSC dispatch fún àyẹ̀wò. A ó jẹ́ kí ẹ mọ̀ bí ó ti ń lọ.",
        (OutboundKind.ReportAck, _) =>
            "Your report is complete and is now with FRSC dispatch for review. We will keep you updated.",

        (OutboundKind.StatusUnderReview, "pidgin") =>
            "Your report dey with FRSC dispatch for review now. We go update you once anything change.",
        (OutboundKind.StatusUnderReview, "yoruba") =>
            "Ìjábọ̀ yín wà lọ́wọ́ FRSC dispatch fún àyẹ̀wò. A ó fún yín ní ìròyìn ní kété tí nǹkan bá yí padà.",
        (OutboundKind.StatusUnderReview, _) =>
            "Your report is with FRSC dispatch for review. We will update you as soon as anything changes.",

        (OutboundKind.DispatchedNotice, "pidgin") =>
            "UPDATE: FRSC don dispatch responders to the incident wey you report. Help dey come.",
        (OutboundKind.DispatchedNotice, "yoruba") =>
            "ÌRÒYÌN: FRSC ti rán àwọn olùdáhùn sí ibi ìjàǹbá tí ẹ jábọ̀. Ìrànlọ́wọ́ ń bọ̀.",
        (OutboundKind.DispatchedNotice, _) =>
            "UPDATE: FRSC has dispatched responders to the incident you reported. Help is on the way.",

        (OutboundKind.ArrivedNotice, "pidgin") =>
            "UPDATE: responders don reach the scene. Thank you for your report.",
        (OutboundKind.ArrivedNotice, "yoruba") =>
            "ÌRÒYÌN: àwọn olùdáhùn ti dé ibi ìṣẹ̀lẹ̀ náà. Ẹ ṣé fún ìjábọ̀ yín.",
        (OutboundKind.ArrivedNotice, _) =>
            "UPDATE: responders have arrived at the scene. Thank you for your report.",

        (OutboundKind.TransportedNotice, "pidgin") =>
            "UPDATE: dem don move the people wey involve go where dem go care for them. Your report make this happen — thank you.",
        (OutboundKind.TransportedNotice, "yoruba") =>
            "ÌRÒYÌN: wọ́n ti gbé àwọn tí ọ̀rọ̀ kàn lọ sí ibi ìtọ́jú. Ìjábọ̀ yín ló mú èyí ṣẹ — ẹ ṣé.",
        (OutboundKind.TransportedNotice, _) =>
            "UPDATE: those involved have been moved for care. Your report made this happen — thank you.",

        (OutboundKind.IncidentAlreadyHandled, "pidgin") =>
            "Thank you — responders don already dey aware of this incident and dem dey handle am.",
        (OutboundKind.IncidentAlreadyHandled, "yoruba") =>
            "Ẹ ṣé — àwọn olùdáhùn ti mọ̀ nípa ìjàǹbá yìí, wọ́n sì ń bójú tó o.",
        (OutboundKind.IncidentAlreadyHandled, _) =>
            "Thank you — responders are already aware of this incident and it is being handled.",
    };
}
