namespace Mawrid.Domain.Common;

/// <summary>
/// An entity that belongs to one organization (tenant). EF Core filters every query
/// on <see cref="OrganizationId"/>, so one organization never sees another's data (ADR-0003).
/// </summary>
public interface ITenantOwned
{
    Guid OrganizationId { get; }
}
