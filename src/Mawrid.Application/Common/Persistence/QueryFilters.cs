namespace Mawrid.Application.Common.Persistence;

/// <summary>Names of the EF Core query filters, for the rare places that must skip one.</summary>
public static class QueryFilters
{
    /// <summary>Hides other organizations' data (ADR-0003). Skip it only for login and token refresh.</summary>
    public const string Tenant = "Tenant";
}
