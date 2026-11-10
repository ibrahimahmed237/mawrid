using Mawrid.Application.Common.Abstractions;

namespace Mawrid.Infrastructure.Time;

/// <summary>The real clock. Built on .NET's <see cref="TimeProvider"/>.</summary>
public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
