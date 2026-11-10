using Mawrid.Application.Common.Abstractions;
using Mawrid.Infrastructure.Time;

namespace Mawrid.Api.Tests.Infrastructure;

/// <summary>
/// The business clock for tests: real time plus an offset. <see cref="Travel"/> moves it forward,
/// for example past an RFQ deadline, and moves it back when disposed. Tokens still use real time.
/// </summary>
public sealed class TestClock : IClock
{
    private TimeSpan _offset;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + _offset;

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, SystemClock.BusinessTimeZone).DateTime);

    public IDisposable Travel(TimeSpan by)
    {
        _offset += by;
        return new Return(this, by);
    }

    private sealed class Return(TestClock clock, TimeSpan by) : IDisposable
    {
        public void Dispose() => clock._offset -= by;
    }
}
