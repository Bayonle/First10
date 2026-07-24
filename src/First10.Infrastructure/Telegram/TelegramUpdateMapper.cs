using System.Text.Json;
using First10.Domain.Channels;

namespace First10.Infrastructure.Telegram;

/// <summary>
/// What the polling service must do with one Telegram update, before media work.
/// FileId is the Telegram file to download when the message carries media;
/// MediaContentType is what the downloaded bytes should be ingested as.
/// </summary>
public sealed record MappedTelegramMessage(
    long ChatId,
    string ExternalMessageId,
    InboundKind Kind,
    string? Text,
    string? FileId,
    string? MediaContentType,
    GeoPoint? Location,
    DateTimeOffset OccurredAt);

/// <summary>
/// Pure translation from a Telegram `update` JSON object to the adapter's work order.
/// No I/O — fully unit-testable against captured payload shapes. Unknown message
/// types are NOT dropped (D-008): they map to a text placeholder so the pipeline
/// answers with guidance instead of silence.
/// </summary>
public static class TelegramUpdateMapper
{
    public static MappedTelegramMessage? Map(JsonElement update)
    {
        if (!update.TryGetProperty("message", out var msg)) return null; // edits, joins, etc.

        var chatId = msg.GetProperty("chat").GetProperty("id").GetInt64();
        var messageId = msg.GetProperty("message_id").GetInt64();
        var externalMessageId = $"{chatId}:{messageId}"; // message_id is per-chat — qualify it
        var occurredAt = DateTimeOffset.FromUnixTimeSeconds(msg.GetProperty("date").GetInt64());
        var caption = msg.TryGetProperty("caption", out var cap) ? cap.GetString() : null;

        if (msg.TryGetProperty("location", out var loc))
        {
            return new MappedTelegramMessage(chatId, externalMessageId, InboundKind.LocationPin,
                null, null, null,
                new GeoPoint(loc.GetProperty("latitude").GetDouble(), loc.GetProperty("longitude").GetDouble()),
                occurredAt);
        }

        if (msg.TryGetProperty("photo", out var photos) && photos.GetArrayLength() > 0)
        {
            // Telegram sends multiple resolutions; the last entry is the largest.
            var largest = photos[photos.GetArrayLength() - 1];
            return new MappedTelegramMessage(chatId, externalMessageId, InboundKind.Image,
                caption, largest.GetProperty("file_id").GetString(), "image/jpeg", null, occurredAt);
        }

        if (msg.TryGetProperty("voice", out var voice))
        {
            return new MappedTelegramMessage(chatId, externalMessageId, InboundKind.Voice,
                caption, voice.GetProperty("file_id").GetString(), "audio/ogg", null, occurredAt);
        }

        if (msg.TryGetProperty("video", out var video))
        {
            // D-019: videos become blurred contact sheets at ingest and enter as Image.
            return new MappedTelegramMessage(chatId, externalMessageId, InboundKind.Image,
                caption, video.GetProperty("file_id").GetString(), "video/mp4", null, occurredAt);
        }

        if (msg.TryGetProperty("video_note", out var note))
        {
            return new MappedTelegramMessage(chatId, externalMessageId, InboundKind.Image,
                caption, note.GetProperty("file_id").GetString(), "video/mp4", null, occurredAt);
        }

        if (msg.TryGetProperty("text", out var text))
        {
            return new MappedTelegramMessage(chatId, externalMessageId, InboundKind.Text,
                text.GetString(), null, null, null, occurredAt);
        }

        // Stickers, documents, contacts, polls… — never silently dropped (D-008):
        // a text placeholder flows through triage and earns the reporter a guided reply.
        return new MappedTelegramMessage(chatId, externalMessageId, InboundKind.Text,
            caption ?? "[unsupported message type]", null, null, null, occurredAt);
    }
}
