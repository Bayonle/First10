using System.Text.Json;
using First10.Domain.Channels;
using First10.Infrastructure.Media;
using First10.Infrastructure.Telegram;
using Wolverine;

namespace First10.Api.Telegram;

/// <summary>
/// The Telegram channel adapter's inbound half: long-polls getUpdates (no public URL,
/// no webhook, works from a laptop and the pilot VM alike) and turns each message into
/// the normalized envelope. Channel dirty work happens HERE, per D-005/D-009: media is
/// downloaded synchronously and pushed through SecureMediaIngest (images blurred,
/// videos → blurred contact sheets, voice passthrough) before anything is published.
///
/// Delivery is at-least-once by design: the offset only advances past an update after
/// it is fully processed, and redelivered updates collapse on the (Channel,
/// ExternalMessageId) dedup index. A single poisoned update is skipped AFTER
/// exhausting local attempts, with a loud log — it must not wedge the whole channel.
/// </summary>
public sealed class TelegramPollingService(
    TelegramBotApi api,
    IServiceProvider services,
    IHostApplicationLifetime lifetime,
    ILogger<TelegramPollingService> logger) : BackgroundService
{
    private const int LongPollSeconds = 25;
    private const int MaxAttemptsPerUpdate = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Same start-order rule as RetentionBootstrapper: no bus before the host is up.
        var started = new TaskCompletionSource();
        await using var startedReg = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await using var stoppedReg = stoppingToken.Register(() => started.TrySetCanceled());
        try
        {
            await started.Task;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        logger.LogInformation("Telegram adapter active (long polling)");
        long offset = 0;
        var attemptsForCurrent = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await api.GetUpdatesAsync(offset, LongPollSeconds, stoppingToken);
                foreach (var update in updates.EnumerateArray())
                {
                    var updateId = update.GetProperty("update_id").GetInt64();
                    try
                    {
                        await ProcessUpdate(update, stoppingToken);
                        offset = updateId + 1;
                        attemptsForCurrent = 0;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        attemptsForCurrent++;
                        if (attemptsForCurrent >= MaxAttemptsPerUpdate)
                        {
                            logger.LogError(e,
                                "Telegram update {UpdateId} failed {Attempts} times — SKIPPED (possible lost report; see payload log)",
                                updateId, attemptsForCurrent);
                            logger.LogError("Skipped Telegram payload: {Payload}", update.GetRawText());
                            offset = updateId + 1; // move past the poison pill
                            attemptsForCurrent = 0;
                        }
                        else
                        {
                            logger.LogWarning(e, "Telegram update {UpdateId} failed (attempt {Attempt}); retrying",
                                updateId, attemptsForCurrent);
                            break; // re-poll from the same offset
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Telegram polling error; retrying in 5s");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task ProcessUpdate(JsonElement update, CancellationToken ct)
    {
        var mapped = TelegramUpdateMapper.Map(update);
        if (mapped is null) return; // not a message (edit, member event…) — nothing to report

        string? mediaRef = null;
        if (mapped.FileId is not null)
        {
            // Synchronous download inside the adapter (the D-012 rule; Telegram file
            // paths also expire) → straight through the blur gate before persistence.
            var filePath = await api.GetFilePathAsync(mapped.FileId, ct)
                ?? throw new InvalidOperationException($"Telegram getFile returned no path for {mapped.FileId}");
            var bytes = await api.DownloadFileAsync(filePath, ct);

            using var scope = services.CreateScope();
            var ingest = scope.ServiceProvider.GetRequiredService<SecureMediaIngest>();
            using var content = new MemoryStream(bytes);
            var result = await ingest.IngestAsync(content, mapped.MediaContentType!, ct);
            mediaRef = result.MediaRef;
        }

        using var busScope = services.CreateScope();
        var bus = busScope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new InboundChannelMessage(
            ChannelKind.Telegram,
            mapped.ChatId.ToString(),
            mapped.ExternalMessageId,
            mapped.Kind,
            mapped.Text,
            mediaRef,
            mapped.Location,
            mapped.OccurredAt));
    }
}
