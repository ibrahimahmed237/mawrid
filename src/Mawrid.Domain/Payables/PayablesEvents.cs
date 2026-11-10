using Mawrid.Domain.Common;

namespace Mawrid.Domain.Payables;

public sealed record SupplierInvoiceReceived(Guid InvoiceId, Guid OrganizationId, Guid SupplierId, Guid PurchaseOrderId) : IDomainEvent;

public sealed record SupplierInvoiceMatched(Guid InvoiceId, Guid OrganizationId) : IDomainEvent;

public sealed record SupplierInvoiceHeld(Guid InvoiceId, Guid OrganizationId, IReadOnlyList<string> HoldCodes) : IDomainEvent;

public sealed record SupplierInvoiceReleased(
    Guid InvoiceId, Guid OrganizationId, Guid SupplierId, Guid PurchaseOrderId, IReadOnlyList<InvoicedQuantity> Quantities, decimal Total) : IDomainEvent;

public sealed record SupplierInvoiceRejected(Guid InvoiceId, Guid OrganizationId, Guid SupplierId, string Reason) : IDomainEvent;

public sealed record InvoicedQuantity(int PoLineNumber, decimal Quantity);
