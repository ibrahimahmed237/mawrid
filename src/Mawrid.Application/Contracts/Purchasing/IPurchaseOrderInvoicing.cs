using Mawrid.Domain.Common;

namespace Mawrid.Application.Contracts.Purchasing;

/// <summary>Published by the Purchasing module for Payables: the PO data an invoice is matched against.</summary>
public interface IPurchaseOrderInvoicing
{
    /// <summary>A PO of the current organization by its number, or null.</summary>
    Task<PurchaseOrderForInvoice?> FindByNumberAsync(string number, CancellationToken cancellationToken);

    Task<PurchaseOrderForInvoice?> FindAsync(Guid purchaseOrderId, CancellationToken cancellationToken);

    /// <summary>Adds the quantities of a released invoice to the PO lines. Saved by the caller.</summary>
    Task<Result> RecordInvoicedAsync(Guid purchaseOrderId, IReadOnlyList<(int LineNumber, decimal Quantity)> quantities, CancellationToken cancellationToken);
}

/// <param name="AcceptsInvoices">True once the PO was sent, until it is cancelled.</param>
public sealed record PurchaseOrderForInvoice(
    Guid Id, string Number, Guid SupplierId, Guid BuyerId, bool AcceptsInvoices, IReadOnlyList<PurchaseOrderLineForInvoice> Lines);

public sealed record PurchaseOrderLineForInvoice(int LineNumber, string Description, decimal UnitPrice, VatCategory VatCategory, decimal AcceptedQuantity);
