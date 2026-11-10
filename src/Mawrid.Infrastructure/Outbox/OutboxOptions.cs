using System.ComponentModel.DataAnnotations;

namespace Mawrid.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Turns the background worker on or off. Tests turn it off and run the processor directly.</summary>
    public bool Enabled { get; init; } = true;

    [Range(typeof(TimeSpan), "00:00:00.100", "00:05:00")]
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(2);

    [Range(1, 500)]
    public int BatchSize { get; init; } = 20;

    /// <summary>After this many failed attempts a message is dead-lettered.</summary>
    [Range(1, 50)]
    public int MaxAttempts { get; init; } = 8;
}
