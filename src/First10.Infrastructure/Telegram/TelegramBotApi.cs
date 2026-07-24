using System.Net.Http.Json;
using System.Text.Json;

namespace First10.Infrastructure.Telegram;

/// <summary>
/// Minimal Telegram Bot API client — exactly the four calls the adapter needs
/// (long-poll updates, resolve + download files, send messages). Hand-rolled over
/// HttpClient: the surface is tiny and a full SDK dependency isn't warranted.
/// </summary>
public sealed class TelegramBotApi(HttpClient http, string botToken)
{
    private string Method(string name) => $"https://api.telegram.org/bot{botToken}/{name}";

    /// <summary>Long-polls for updates. Blocks server-side up to <paramref name="timeoutSeconds"/>.</summary>
    public async Task<JsonElement> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            Method("getUpdates"),
            new { offset, timeout = timeoutSeconds, allowed_updates = new[] { "message" } },
            ct);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.GetProperty("result").Clone();
    }

    /// <summary>
    /// Clears any registered webhook. getUpdates returns 409 while a webhook is
    /// active, so a polling adapter must claim the bot at startup.
    /// </summary>
    public async Task DeleteWebhookAsync(CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            Method("deleteWebhook"), new { drop_pending_updates = false }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task SendMessageAsync(long chatId, string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            Method("sendMessage"), new { chat_id = chatId, text }, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Resolves a file_id to a downloadable path (Telegram's two-step download).</summary>
    public async Task<string?> GetFilePathAsync(string fileId, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(Method("getFile"), new { file_id = fileId }, ct);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.GetProperty("result").TryGetProperty("file_path", out var p) ? p.GetString() : null;
    }

    public async Task<byte[]> DownloadFileAsync(string filePath, CancellationToken ct) =>
        await http.GetByteArrayAsync($"https://api.telegram.org/file/bot{botToken}/{filePath}", ct);
}
