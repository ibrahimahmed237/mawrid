using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Contracts.Requisitions;
using Mawrid.Domain.Common;
using Mawrid.Domain.Requisitions;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Requisitions;

internal sealed class RequisitionSourcing(IAppDbContext db) : IRequisitionSourcing
{
    public async Task<Result<RequisitionsForSourcing>> GetForRfqAsync(IReadOnlyCollection<Guid> requisitionIds, CancellationToken cancellationToken)
    {
        var ids = requisitionIds.Distinct().ToList();
        var requisitions = await db.Requisitions.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken);

        var valid = requisitions.Count == ids.Count && ids.Count > 0
            && requisitions.All(r => r.Status == RequisitionStatus.InSourcing && r.SourcingRoute == SourcingRoute.Rfq)
            && requisitions.Select(r => r.CostCenterId).Distinct().Count() == 1;

        return valid ? ToSourcing(requisitions) : RequisitionErrors.NotReadyForSourcing;
    }

    public async Task<Result<RequisitionsForSourcing>> GetForDirectPurchaseAsync(Guid requisitionId, CancellationToken cancellationToken)
    {
        var requisition = await db.Requisitions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == requisitionId, cancellationToken);
        return requisition is { Status: RequisitionStatus.InSourcing, SourcingRoute: SourcingRoute.DirectPurchase }
            ? ToSourcing([requisition])
            : RequisitionErrors.NotReadyForSourcing;
    }

    public async Task MarkOrderedAsync(IReadOnlyCollection<Guid> requisitionIds, CancellationToken cancellationToken)
    {
        var requisitions = await db.Requisitions.Where(r => requisitionIds.Contains(r.Id)).ToListAsync(cancellationToken);
        foreach (var requisition in requisitions.Where(r => r.Status == RequisitionStatus.InSourcing))
        {
            requisition.MarkOrdered();
        }
    }

    private static RequisitionsForSourcing ToSourcing(IReadOnlyList<PurchaseRequisition> requisitions) => new(
        requisitions[0].CostCenterId,
        requisitions.OrderBy(r => r.Number)
            .SelectMany(r => r.Lines.OrderBy(l => l.LineNumber).Select(l =>
                new RequisitionLineForSourcing(r.Id, l.LineNumber, l.Description, l.Quantity, l.Unit, l.EstimatedUnitPrice, l.PreferredSupplierId)))
            .ToList());
}
