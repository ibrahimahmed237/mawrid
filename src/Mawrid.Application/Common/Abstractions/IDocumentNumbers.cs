namespace Mawrid.Application.Common.Abstractions;

/// <summary>
/// Gives the next human-friendly document number per organization, prefix and year,
/// such as PR-2026-000012. Numbers are unique; a failed save can leave a gap.
/// </summary>
public interface IDocumentNumbers
{
    Task<string> NextAsync(string prefix, CancellationToken cancellationToken);
}
