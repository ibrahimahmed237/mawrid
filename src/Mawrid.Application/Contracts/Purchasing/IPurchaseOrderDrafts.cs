using Mawrid.Domain.Common;

namespace Mawrid.Application.Contracts.Purchasing;

/// <summary>Published by the Purchasing module: create draft purchase orders from an award or a direct purchase.</summary>
public interface IPurchaseOrderDrafts
{
    /// <summary>Adds a draft PO to the unit of work; the caller saves.</summary>
    Task<Result<Guid>> CreateDraftAsync(PurchaseOrderDraft draft, CancellationToken cancellationToken);
}

public sealed record PurchaseOrderDraft(
    Guid SupplierId,
    Guid CostCenterId,
    string Source,
    Guid SourceId,
    IReadOnlyList<Guid> RequisitionIds,
    IReadOnlyList<PurchaseOrderDraftLine> Lines);

public sealed record PurchaseOrderDraftLine(string Description, decimal Quantity, string Unit, decimal UnitPrice, VatCategory VatCategory);

public static class PurchaseOrderSources
{
    public const string Rfq = nameof(Rfq);
    public const string DirectPurchase = nameof(DirectPurchase);
}
