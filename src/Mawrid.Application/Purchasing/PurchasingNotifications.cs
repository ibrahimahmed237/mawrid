using Mawrid.Application.Common.Events;
using Mawrid.Application.Common.Notifications;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Contracts.Identity;
using Mawrid.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Purchasing;

/// <summary>Tells the supplier about sent or cancelled POs, and the buyer about acknowledgements and deliveries.</summary>
internal sealed class PurchasingNotifications(IAppDbContext db, IOrganizationDirectory directory, INotificationSender notifications) :
    INotificationHandler<DomainEventNotification<PurchaseOrderSent>>,
    INotificationHandler<DomainEventNotification<PurchaseOrderCancelled>>,
    INotificationHandler<DomainEventNotification<PurchaseOrderAcknowledged>>,
    INotificationHandler<DomainEventNotification<GoodsReceived>>
{
    public async Task Handle(DomainEventNotification<PurchaseOrderSent> notification, CancellationToken cancellationToken)
    {
        var order = await FindAsync(notification.Event.PurchaseOrderId, cancellationToken);
        if (order is not null)
        {
            await NotifySupplierAsync(order, "purchase_order.sent", $"New purchase order {order.Number}",
                $"Please confirm {order.Number} ({order.Total:N2} SAR incl. VAT).", cancellationToken);
        }
    }

    public async Task Handle(DomainEventNotification<PurchaseOrderCancelled> notification, CancellationToken cancellationToken)
    {
        // The supplier knows about a PO only once it was sent.
        var order = await FindAsync(notification.Event.PurchaseOrderId, cancellationToken);
        if (order is { SentAtUtc: not null })
        {
            await NotifySupplierAsync(order, "purchase_order.cancelled", $"Purchase order {order.Number} cancelled",
                $"{order.Number} was cancelled: {order.CancellationReason}", cancellationToken);
        }
    }

    public async Task Handle(DomainEventNotification<PurchaseOrderAcknowledged> notification, CancellationToken cancellationToken)
    {
        var order = await FindAsync(notification.Event.PurchaseOrderId, cancellationToken);
        if (order is not null)
        {
            await NotifyBuyerAsync(order, "purchase_order.acknowledged", $"{order.Number} confirmed by the supplier",
                $"The supplier confirmed {order.Number}.", cancellationToken);
        }
    }

    public async Task Handle(DomainEventNotification<GoodsReceived> notification, CancellationToken cancellationToken)
    {
        var order = await FindAsync(notification.Event.PurchaseOrderId, cancellationToken);
        if (order is not null)
        {
            var what = notification.Event.FullyReceived ? "fully received" : "partly received";
            await NotifyBuyerAsync(order, "purchase_order.received", $"{order.Number} {what}", $"A delivery for {order.Number} was recorded.",
                cancellationToken);
        }
    }

    private Task<PurchaseOrder?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    private async Task NotifySupplierAsync(PurchaseOrder order, string type, string title, string message, CancellationToken cancellationToken)
    {
        var users = await directory.SupplierUsersAsync([order.SupplierId], cancellationToken);
        await notifications.SendAsync(users, new UserNotification(type, title, message, $"/api/v1/supplier/purchase-orders/{order.Id}"), cancellationToken);
    }

    private Task NotifyBuyerAsync(PurchaseOrder order, string type, string title, string message, CancellationToken cancellationToken) =>
        notifications.SendAsync([order.BuyerId], new UserNotification(type, title, message, $"/api/v1/purchase-orders/{order.Id}"), cancellationToken);
}
