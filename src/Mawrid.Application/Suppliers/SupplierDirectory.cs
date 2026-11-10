using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Contracts.Suppliers;
using Mawrid.Domain.Suppliers;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Suppliers;

internal sealed class SupplierDirectory(IAppDbContext db, IClock clock) : ISupplierDirectory
{
    public async Task<SupplierInfo?> FindAsync(Guid supplierId, CancellationToken cancellationToken) =>
        (await FindManyAsync([supplierId], cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyList<SupplierInfo>> FindManyAsync(IReadOnlyCollection<Guid> supplierIds, CancellationToken cancellationToken)
    {
        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(s => supplierIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        var today = clock.Today;
        return suppliers.Select(s => ToInfo(s, today)).ToList();
    }

    private static SupplierInfo ToInfo(Supplier supplier, DateOnly today) => new(
        supplier.Id, supplier.NameEn, supplier.NameAr, supplier.VatNumber.Value, supplier.IsAwardable(today), supplier.HasPendingBankChange);
}
