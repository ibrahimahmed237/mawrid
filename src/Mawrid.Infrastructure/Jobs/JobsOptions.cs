namespace Mawrid.Infrastructure.Jobs;

/// <summary>Settings for background jobs (section "Jobs").</summary>
public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary>Runs the Hangfire server and schedules the recurring jobs. Tests turn it off and call the jobs directly.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How long processed outbox messages are kept for troubleshooting.</summary>
    public TimeSpan OutboxRetention { get; set; } = TimeSpan.FromDays(7);
}
