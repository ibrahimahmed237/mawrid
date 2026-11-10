using Mawrid.Domain.Common;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Identity;
using Mawrid.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Identity;

internal sealed class OrganizationDirectory(IAppDbContext db, ICurrentUser currentUser) : IOrganizationDirectory
{
    public async Task<ProcurementSettingsInfo> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == currentUser.OrganizationId)
            .Select(o => o.Settings)
            .SingleAsync(cancellationToken);

        return new ProcurementSettingsInfo(settings.DirectPurchaseLimit, settings.MinimumSuppliersPerRfq, settings.MinimumRfqWindowDays,
            settings.PriceTolerancePercent, settings.PriceToleranceMaxAmount, settings.OverDeliveryPercent);
    }

    public Task<bool> IsActiveCostCenterAsync(Guid costCenterId, CancellationToken cancellationToken) =>
        db.CostCenters.AnyAsync(c => c.Id == costCenterId && c.IsActive, cancellationToken);

    public async Task<IReadOnlyCollection<Guid>> CostCenterApproversAsync(Guid costCenterId, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.SupplierId == null
                && u.Roles.Any(r => r.Role == Role.Approver && (r.CostCenterId == costCenterId || r.CostCenterId == null)))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<Guid>> ActiveInternalUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.IsActive && u.SupplierId == null)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
}
