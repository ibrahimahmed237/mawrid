using Mawrid.Domain.Files;
using Mawrid.Domain.Identity;
using Mawrid.Domain.Suppliers;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Common.Persistence;

/// <summary>
/// The database as seen by handlers (ADR-0008). Every set of tenant-owned data is
/// already filtered to the current organization.
/// </summary>
public interface IAppDbContext
{
    DbSet<Organization> Organizations { get; }

    DbSet<CostCenter> CostCenters { get; }

    DbSet<User> Users { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<StoredFile> Files { get; }

    DbSet<Supplier> Suppliers { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
