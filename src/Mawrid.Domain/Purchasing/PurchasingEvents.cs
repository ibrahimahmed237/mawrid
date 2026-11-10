using Mawrid.Domain.Common;

namespace Mawrid.Domain.Purchasing;

public sealed record PurchaseOrderCreated(Guid PurchaseOrderId, Guid OrganizationId, Guid SupplierId) : IDomainEvent;

public sealed record PurchaseOrderSubmitted(Guid PurchaseOrderId, Guid OrganizationId, Guid ApprovalRequestId) : IDomainEvent;

public sealed record PurchaseOrderApproved(Guid PurchaseOrderId, Guid OrganizationId, Guid SupplierId, decimal Total) : IDomainEvent;

public sealed record PurchaseOrderRejected(Guid PurchaseOrderId, Guid OrganizationId) : IDomainEvent;

public sealed record PurchaseOrderSent(Guid PurchaseOrderId, Guid OrganizationId, Guid SupplierId) : IDomainEvent;

public sealed record PurchaseOrderAcknowledged(Guid PurchaseOrderId, Guid OrganizationId) : IDomainEvent;

public sealed record GoodsReceived(Guid PurchaseOrderId, Guid OrganizationId, Guid ReceiptId, bool FullyReceived) : IDomainEvent;

public sealed record PurchaseOrderCancelled(Guid PurchaseOrderId, Guid OrganizationId, Guid? ApprovalRequestId) : IDomainEvent;

public sealed record PurchaseOrderClosed(Guid PurchaseOrderId, Guid OrganizationId) : IDomainEvent;
