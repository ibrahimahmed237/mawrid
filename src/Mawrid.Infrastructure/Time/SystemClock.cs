using Mawrid.Application.Common.Abstractions;

namespace Mawrid.Infrastructure.Time;

/// <summary>The real clock. Built on .NET's <see cref="TimeProvider"/>.</summary>
public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    /// <summary>Saudi Arabia has no daylight saving time; the offset is always UTC+3.</summary>
    public static readonly TimeZoneInfo BusinessTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, BusinessTimeZone).DateTime);
}
