using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mawrid.Infrastructure.Jobs;

/// <summary>
/// Creates or updates the recurring jobs once the database can be reached. It runs in the
/// background, so the app still starts (and reports "not ready") while SQL Server is down.
/// </summary>
internal sealed partial class RecurringJobRegistration(IServiceProvider services, ILogger<RecurringJobRegistration> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(5);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                RecurringJobSchedule.ScheduleRecurringJobs(services);
                LogScheduled(RecurringJobSchedule.Jobs.Count);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogRetry(exception, delay);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 300));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduled {Count} recurring jobs")]
    private partial void LogScheduled(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not schedule recurring jobs; retrying in {Delay}")]
    private partial void LogRetry(Exception exception, TimeSpan delay);
}
