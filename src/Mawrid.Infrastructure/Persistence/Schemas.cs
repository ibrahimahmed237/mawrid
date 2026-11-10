namespace Mawrid.Infrastructure.Persistence;

/// <summary>One database schema per module, so module boundaries are visible in the database too.</summary>
public static class Schemas
{
    public const string Identity = "identity";
    public const string Platform = "platform";
    public const string Files = "files";
    public const string Suppliers = "suppliers";
    public const string Approvals = "approvals";
    public const string Requisitions = "requisitions";
    public const string Sourcing = "sourcing";
    public const string Purchasing = "purchasing";
    public const string Payables = "payables";
    public const string Integrations = "integrations";
}
