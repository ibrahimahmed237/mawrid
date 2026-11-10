namespace Mawrid.Application.Common.Abstractions;

/// <summary>
/// The current time. Code never calls <c>DateTime.UtcNow</c> directly, so tests can
/// control time (for example, to test what happens right after an RFQ deadline).
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// Today's business date in Saudi Arabia (Asia/Riyadh). Use it for dates people
    /// think in, such as document expiry. Store and compare times in UTC.
    /// </summary>
    DateOnly Today { get; }
}
