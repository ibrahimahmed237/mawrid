namespace Mawrid.Application.Common.Abstractions;

/// <summary>
/// The current time. Code never calls <c>DateTime.UtcNow</c> directly, so tests can
/// control time (for example, to test what happens right after an RFQ deadline).
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
