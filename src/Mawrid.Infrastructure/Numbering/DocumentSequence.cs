namespace Mawrid.Infrastructure.Numbering;

/// <summary>The last number given for one organization, prefix and year (for example PR, 2026).</summary>
public sealed class DocumentSequence
{
    public Guid OrganizationId { get; init; }

    public required string Prefix { get; init; }

    public int Year { get; init; }

    public int LastValue { get; set; }
}
