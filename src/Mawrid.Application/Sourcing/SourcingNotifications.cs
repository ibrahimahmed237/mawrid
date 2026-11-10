using Mawrid.Application.Common.Events;
using Mawrid.Application.Common.Notifications;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Contracts.Identity;
using Mawrid.Domain.Sourcing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Sourcing;

/// <summary>Tells invited suppliers about RFQ changes, and the buyer when an RFQ closes.</summary>
internal sealed class SourcingNotifications(IAppDbContext db, IOrganizationDirectory directory, INotificationSender notifications) :
    INotificationHandler<DomainEventNotification<RfqPublished>>,
    INotificationHandler<DomainEventNotification<RfqAmended>>,
    INotificationHandler<DomainEventNotification<RfqCancelled>>,
    INotificationHandler<DomainEventNotification<RfqClosed>>,
    INotificationHandler<DomainEventNotification<RfqAwarded>>
{
    public async Task Handle(DomainEventNotification<RfqPublished> notification, CancellationToken cancellationToken)
    {
        var e = notification.Event;
        await NotifySuppliersAsync(e.RfqId, e.InvitedSupplierIds, "rfq.invited",
            rfq => ($"New RFQ {rfq.Number}: {rfq.Title}", $"You are invited to quote for {rfq.Number}. Deadline: {e.Deadline:yyyy-MM-dd HH:mm} UTC."),
            cancellationToken);
    }

    public async Task Handle(DomainEventNotification<RfqAmended> notification, CancellationToken cancellationToken)
    {
        var e = notification.Event;
        await NotifySuppliersAsync(e.RfqId, null, "rfq.amended",
            rfq => ($"RFQ {rfq.Number} changed", $"{rfq.Number} was changed. Please check it and confirm your quotation by {e.Deadline:yyyy-MM-dd HH:mm} UTC."),
            cancellationToken);
    }

    public async Task Handle(DomainEventNotification<RfqCancelled> notification, CancellationToken cancellationToken) =>
        await NotifySuppliersAsync(notification.Event.RfqId, notification.Event.InvitedSupplierIds, "rfq.cancelled",
            rfq => ($"RFQ {rfq.Number} cancelled", $"{rfq.Number} was cancelled: {rfq.CancellationReason}"), cancellationToken);

    public async Task Handle(DomainEventNotification<RfqClosed> notification, CancellationToken cancellationToken)
    {
        var rfq = await db.Rfqs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == notification.Event.RfqId, cancellationToken);
        if (rfq is null)
        {
            return;
        }

        await notifications.SendAsync([rfq.BuyerId], new UserNotification(
            "rfq.closed", $"RFQ {rfq.Number} closed", $"The deadline of {rfq.Number} passed. The quotations can now be compared.",
            $"/api/v1/rfqs/{rfq.Id}/comparison"), cancellationToken);
    }

    public async Task Handle(DomainEventNotification<RfqAwarded> notification, CancellationToken cancellationToken)
    {
        var e = notification.Event;
        await NotifySuppliersAsync(e.RfqId, e.WinningSupplierIds, "rfq.won",
            rfq => ($"RFQ {rfq.Number}: you won", $"You were awarded items in {rfq.Number}. A purchase order follows after approval."),
            cancellationToken);

        // Default policy: suppliers who did not win are told the result, not the winner or the prices.
        await NotifySuppliersAsync(e.RfqId, e.InvitedSupplierIds.Except(e.WinningSupplierIds).ToList(), "rfq.not_won",
            rfq => ($"RFQ {rfq.Number} awarded", $"{rfq.Number} was awarded to another supplier. Thank you for your quotation."),
            cancellationToken);
    }

    /// <param name="supplierIds">Who to tell; null means every invited supplier.</param>
    private async Task NotifySuppliersAsync(
        Guid rfqId, IReadOnlyCollection<Guid>? supplierIds, string type, Func<Rfq, (string Title, string Message)> text, CancellationToken cancellationToken)
    {
        var rfq = await db.Rfqs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == rfqId, cancellationToken);
        supplierIds ??= rfq?.Invitations.Select(i => i.SupplierId).ToList();
        if (rfq is null || supplierIds is null || supplierIds.Count == 0)
        {
            return;
        }

        var users = await directory.SupplierUsersAsync(supplierIds, cancellationToken);
        var (title, message) = text(rfq);
        await notifications.SendAsync(users, new UserNotification(type, title, message, $"/api/v1/supplier/rfqs/{rfq.Id}"), cancellationToken);
    }
}
