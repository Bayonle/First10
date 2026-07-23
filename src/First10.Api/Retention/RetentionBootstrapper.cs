using First10.Application.Retention;
using First10.Domain;
using First10.Infrastructure.Persistence;
using Wolverine;

namespace First10.Api.Retention;

/// <summary>
/// Starts the retention-sweep chain once the host (and therefore Wolverine) is running —
/// scheduling from Program's startup block throws WolverineHasNotStartedException.
/// Rotates the chain id so ticks scheduled by previous deploys die as no-ops, then
/// schedules this process's first sweep.
/// </summary>
public sealed class RetentionBootstrapper(
    IServiceProvider services, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Hosted-service start order is not guaranteed relative to Wolverine's runtime;
        // wait for the fully-started host before touching the bus.
        var started = new TaskCompletionSource();
        await using var startedReg = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await using var stoppedReg = stoppingToken.Register(() => started.TrySetCanceled());
        try
        {
            await started.Task;
        }
        catch (OperationCanceledException)
        {
            return; // shut down before it ever started
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<First10DbContext>();

        var chainId = Guid.NewGuid();
        var chainRow = await db.SystemState.FindAsync([RetentionSweepHandler.ChainKey], stoppingToken);
        if (chainRow is null)
        {
            db.SystemState.Add(new SystemStateEntry
            {
                Key = RetentionSweepHandler.ChainKey,
                Value = chainId.ToString(),
            });
        }
        else
        {
            chainRow.Value = chainId.ToString();
        }
        await db.SaveChangesAsync(stoppingToken);

        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.ScheduleAsync(new RetentionSweepDue(chainId), TimeSpan.FromSeconds(30));
    }
}
