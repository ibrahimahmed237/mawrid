using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mawrid.Infrastructure.Outbox;

/// <summary>Background loop that runs the <see cref="OutboxProcessor"/> every polling interval.</summary>
public sealed partial class OutboxWorker(
    OutboxProcessor processor,
    IOptions<OutboxOptions> options,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.PollingInterval);
        do
        {
            try
            {
                // Keep going while there is a backlog; wait for the timer when the outbox is empty.
                while (await processor.ProcessBatchAsync(stoppingToken) == options.Value.BatchSize)
                {
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCycleFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox cycle failed; will retry on the next tick")]
    private partial void LogCycleFailed(Exception exception);
}
