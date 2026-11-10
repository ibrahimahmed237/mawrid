using Mawrid.Application.Common.Abstractions;
using Mawrid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mawrid.Infrastructure.Outbox;

/// <summary>
/// Degraded when an outbox message has waited too long: notifications and webhooks are late.
/// The app can still serve requests, so this is "degraded", not "unhealthy".
/// </summary>
public sealed class OutboxHealthCheck(AppDbContext db, IClock clock) : IHealthCheck
{
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var limit = clock.UtcNow - MaxDelay;
        var oldest = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.ProcessedOnUtc == null && m.NextAttemptAtUtc < limit)
            .OrderBy(m => m.NextAttemptAtUtc)
            .Select(m => (DateTimeOffset?)m.NextAttemptAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return oldest is null
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded($"An outbox message has waited since {oldest:O}.");
    }
}
