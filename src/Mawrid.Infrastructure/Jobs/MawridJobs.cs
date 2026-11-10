using Hangfire;
using Mawrid.Application.Approvals;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Sourcing;
using Mawrid.Application.Suppliers;
using Mawrid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mawrid.Infrastructure.Jobs;

/// <summary>
/// The recurring jobs. Hangfire calls these methods on the schedule in <see cref="RecurringJobSchedule"/>.
/// No automatic retries: every job runs again on its next schedule, and a retry could send reminders twice.
/// </summary>
[AutomaticRetry(Attempts = 0)]
[DisableConcurrentExecution(timeoutInSeconds: 60)]
public sealed partial class MawridJobs(
    TenantJobRunner runner, AppDbContext db, IClock clock, IOptions<JobsOptions> options, ILogger<MawridJobs> logger)
{
    public Task<int> CloseDueRfqsAsync(CancellationToken cancellationToken) =>
        runner.RunForEachOrganizationAsync(() => new CloseDueRfqsCommand(), cancellationToken);

    public Task<int> WarnExpiringSupplierDocumentsAsync(CancellationToken cancellationToken) =>
        runner.RunForEachOrganizationAsync(() => new WarnExpiringSupplierDocumentsCommand(), cancellationToken);

    public Task<int> SendApprovalRemindersAsync(CancellationToken cancellationToken) =>
        runner.RunForEachOrganizationAsync(() => new SendApprovalRemindersCommand(), cancellationToken);

    /// <summary>Deletes processed outbox messages after the retention period and expired idempotency records.</summary>
    public async Task<int> CleanUpAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var outboxBefore = now - options.Value.OutboxRetention;

        var outbox = await db.OutboxMessages
            .Where(m => m.ProcessedOnUtc != null && m.ProcessedOnUtc < outboxBefore)
            .ExecuteDeleteAsync(cancellationToken);

        var idempotency = await db.IdempotencyRecords
            .IgnoreQueryFilters()
            .Where(r => r.ExpiresAtUtc < now)
            .ExecuteDeleteAsync(cancellationToken);

        LogCleanedUp(outbox, idempotency);
        return outbox + idempotency;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Clean-up deleted {OutboxMessages} outbox messages and {IdempotencyRecords} idempotency records")]
    private partial void LogCleanedUp(int outboxMessages, int idempotencyRecords);
}
