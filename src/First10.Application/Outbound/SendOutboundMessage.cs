namespace First10.Application.Outbound;

public enum OutboundKind
{
    /// <summary>Elicitation: "send a photo and your location pin" (D-008 CHALLENGE).</summary>
    ElicitationChallenge = 0,
    /// <summary>Non-incident reply (greeting/question) — "this line is for crash reports".</summary>
    CannedReply = 1,
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
    string Language);

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
    };
}
