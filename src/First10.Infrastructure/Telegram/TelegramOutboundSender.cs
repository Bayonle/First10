using First10.Domain.Abstractions;
using First10.Domain.Channels;
using Microsoft.Extensions.Logging;

namespace First10.Infrastructure.Telegram;

/// <summary>Delivers outbound texts to Telegram chats (ExternalUserId = chat id).</summary>
public sealed class TelegramOutboundSender(TelegramBotApi api, ILogger<TelegramOutboundSender> logger)
    : IOutboundChannelSender
{
    public ChannelKind Channel => ChannelKind.Telegram;

    public async Task SendAsync(string externalUserId, string text, CancellationToken ct)
    {
        if (!long.TryParse(externalUserId, out var chatId))
        {
            // A malformed id can never succeed — log loudly, don't poison the retry queue.
            logger.LogError("Telegram outbound with non-numeric chat id '{Id}' dropped", externalUserId);
            return;
        }
        await api.SendMessageAsync(chatId, text, ct);
    }
}
