using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Purchasing;
using Mawrid.Domain.Common;
using Mawrid.Domain.Purchasing;

namespace Mawrid.Application.Purchasing;

internal sealed class PurchaseOrderDrafts(IAppDbContext db, ICurrentUser currentUser, IDocumentNumbers numbers, IClock clock) : IPurchaseOrderDrafts
{
    public async Task<Result<Guid>> CreateDraftAsync(PurchaseOrderDraft draft, CancellationToken cancellationToken)
    {
        var lines = draft.Lines.Select(l => new PurchaseOrderLineInput(l.Description, l.Quantity, l.Unit, l.UnitPrice, l.VatCategory)).ToList();
        var source = Enum.Parse<PurchaseOrderSource>(draft.Source);

        var number = await numbers.NextAsync("PO", cancellationToken);
        var created = PurchaseOrder.CreateDraft(currentUser.OrganizationId!.Value, number, draft.SupplierId, draft.CostCenterId,
            currentUser.UserId!.Value, source, draft.SourceId, draft.RequisitionIds, lines, clock.Today);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        db.PurchaseOrders.Add(created.Value);
        return created.Value.Id;
    }
}
