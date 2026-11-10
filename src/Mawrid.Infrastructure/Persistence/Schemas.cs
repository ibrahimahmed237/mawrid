namespace Mawrid.Infrastructure.Persistence;

/// <summary>One database schema per module, so module boundaries are visible in the database too.</summary>
public static class Schemas
{
    public const string Identity = "identity";
    public const string Platform = "platform";
}
