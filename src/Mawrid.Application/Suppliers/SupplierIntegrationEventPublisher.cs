using Mawrid.Application.Common.Events;
using Mawrid.Application.Contracts.Suppliers;
using Mawrid.Domain.Suppliers;
using MediatR;

namespace Mawrid.Application.Suppliers;

/// <summary>Turns Suppliers domain events into integration events for other modules.</summary>
internal sealed class SupplierIntegrationEventPublisher(IPublisher publisher) : INotificationHandler<DomainEventNotification<SupplierBankAccountVerified>>
{
    public Task Handle(DomainEventNotification<SupplierBankAccountVerified> notification, CancellationToken cancellationToken) =>
        publisher.Publish(new SupplierBankAccountVerifiedIntegrationEvent(notification.Event.OrganizationId, notification.Event.SupplierId), cancellationToken);
}
