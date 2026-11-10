using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Mawrid.Infrastructure.Jobs;

/// <summary>When each recurring job runs. Times are in Asia/Riyadh; the work week is Sunday to Thursday.</summary>
public static class RecurringJobSchedule
{
    public static readonly TimeZoneInfo Riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

    public static IReadOnlyList<(string Id, string Cron, string Description)> Jobs { get; } =
    [
        ("rfqs-close-due", "*/5 * * * *", "Every 5 minutes: close RFQs whose deadline passed"),
        ("suppliers-document-expiry", "0 6 * * *", "Daily 06:00: warn about expiring supplier documents"),
        ("approvals-reminders", "0 8 * * 0-4", "Sunday to Thursday 08:00: remind approvers about steps waiting a day or more"),
        ("clean-up", "30 3 * * *", "Daily 03:30: delete old outbox messages and expired idempotency records"),
    ];

    /// <summary>Creates or updates the recurring jobs. Safe to call on every start.</summary>
    public static void ScheduleRecurringJobs(IServiceProvider services)
    {
        var manager = services.GetRequiredService<IRecurringJobManager>();
        var options = new RecurringJobOptions { TimeZone = Riyadh };

        manager.AddOrUpdate<MawridJobs>("rfqs-close-due", j => j.CloseDueRfqsAsync(CancellationToken.None), Cron("rfqs-close-due"), options);
        manager.AddOrUpdate<MawridJobs>("suppliers-document-expiry", j => j.WarnExpiringSupplierDocumentsAsync(CancellationToken.None),
            Cron("suppliers-document-expiry"), options);
        manager.AddOrUpdate<MawridJobs>("approvals-reminders", j => j.SendApprovalRemindersAsync(CancellationToken.None), Cron("approvals-reminders"), options);
        manager.AddOrUpdate<MawridJobs>("clean-up", j => j.CleanUpAsync(CancellationToken.None), Cron("clean-up"), options);
    }

    private static string Cron(string id) => Jobs.Single(j => j.Id == id).Cron;
}
