using MediatR;

namespace Mawrid.Application.Contracts.Purchasing;

// Integration events: what Purchasing tells other modules. They are published in-process while
// the outbox delivers the matching domain event, so other modules never see Purchasing's entities.

/// <summary>Goods were received for a PO; invoices waiting for them can be checked again.</summary>
public sealed record GoodsReceivedIntegrationEvent(Guid OrganizationId, Guid PurchaseOrderId) : INotification;

/// <summary>A PO was approved. The ERP receives it by webhook.</summary>
public sealed record PurchaseOrderApprovedIntegrationEvent(
    Guid OrganizationId,
    Guid PurchaseOrderId,
    string Number,
    Guid SupplierId,
    Guid CostCenterId,
    int PaymentTermsDays,
    decimal NetTotal,
    decimal VatTotal,
    decimal Total,
    IReadOnlyList<PurchaseOrderApprovedLine> Lines) : INotification;

public sealed record PurchaseOrderApprovedLine(int LineNumber, string Description, decimal Quantity, string Unit, decimal UnitPrice, string VatCategory);
