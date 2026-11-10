using Microsoft.EntityFrameworkCore;

namespace Mawrid.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the app. Each module adds its entity
/// configurations as <see cref="IEntityTypeConfiguration{TEntity}"/> classes
/// in this assembly; they are picked up automatically.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money totals use 2 decimals by default. Unit prices override this with 4 (DESIGN 6.8).
        configurationBuilder.Properties<decimal>().HavePrecision(19, 2);
    }
}
