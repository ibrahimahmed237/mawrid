using Mawrid.Application.Common.Events;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Contracts.Purchasing;
using Mawrid.Domain.Common;
using Mawrid.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Purchasing;

internal sealed class PurchaseOrderInvoicing(IAppDbContext db) : IPurchaseOrderInvoicing
{
    public async Task<PurchaseOrderForInvoice?> FindByNumberAsync(string number, CancellationToken cancellationToken) =>
        Map(await db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Number == number, cancellationToken));

    public async Task<PurchaseOrderForInvoice?> FindAsync(Guid purchaseOrderId, CancellationToken cancellationToken) =>
        Map(await db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == purchaseOrderId, cancellationToken));

    public async Task<Result> RecordInvoicedAsync(
        Guid purchaseOrderId, IReadOnlyList<(int LineNumber, decimal Quantity)> quantities, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.SingleOrDefaultAsync(o => o.Id == purchaseOrderId, cancellationToken);
        if (order is null)
        {
            return PurchasingErrors.NotFound;
        }

        foreach (var (lineNumber, quantity) in quantities)
        {
            var result = order.RecordInvoiced(lineNumber, quantity);
            if (result.IsFailure)
            {
                return result;
            }
        }

        return Result.Success();
    }

    private static PurchaseOrderForInvoice? Map(PurchaseOrder? order) => order is null
        ? null
        : new PurchaseOrderForInvoice(order.Id, order.Number, order.SupplierId, order.BuyerId,
            AcceptsInvoices: order.SentAtUtc is not null && order.Status != PurchaseOrderStatus.Cancelled,
            order.Lines.OrderBy(l => l.LineNumber)
                .Select(l => new PurchaseOrderLineForInvoice(l.LineNumber, l.Description, l.UnitPrice, l.VatCategory, l.AcceptedQuantity))
                .ToList());
}

/// <summary>Turns Purchasing domain events into integration events for other modules.</summary>
internal sealed class PurchasingIntegrationEventPublisher(IAppDbContext db, IPublisher publisher) :
    INotificationHandler<DomainEventNotification<GoodsReceived>>,
    INotificationHandler<DomainEventNotification<PurchaseOrderApproved>>
{
    public Task Handle(DomainEventNotification<GoodsReceived> notification, CancellationToken cancellationToken) =>
        publisher.Publish(new GoodsReceivedIntegrationEvent(notification.Event.OrganizationId, notification.Event.PurchaseOrderId), cancellationToken);

    public async Task Handle(DomainEventNotification<PurchaseOrderApproved> notification, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == notification.Event.PurchaseOrderId, cancellationToken);
        if (order is null)
        {
            return;
        }

        await publisher.Publish(new PurchaseOrderApprovedIntegrationEvent(
            order.OrganizationId, order.Id, order.Number, order.SupplierId, order.CostCenterId, order.PaymentTermsDays,
            order.NetTotal, order.VatTotal, order.Total,
            order.Lines.OrderBy(l => l.LineNumber)
                .Select(l => new PurchaseOrderApprovedLine(l.LineNumber, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.VatCategory.ToString()))
                .ToList()), cancellationToken);
    }
}
