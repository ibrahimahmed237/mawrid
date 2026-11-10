namespace Mawrid.Infrastructure.Seeding;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Creates the first organization and its admin when the database has no organization yet.</summary>
    public BootstrapOptions? Bootstrap { get; init; }

    /// <summary>Adds demo cost centers and one user per role. Development only.</summary>
    public bool DemoData { get; init; }
}

public sealed class BootstrapOptions
{
    public string OrganizationName { get; init; } = string.Empty;

    public string AdminEmail { get; init; } = string.Empty;

    public string AdminName { get; init; } = "Administrator";

    public string AdminPassword { get; init; } = string.Empty;
}
