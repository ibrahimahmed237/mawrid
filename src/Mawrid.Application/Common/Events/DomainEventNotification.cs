using Mawrid.Domain.Common;
using MediatR;

namespace Mawrid.Application.Common.Events;

/// <summary>
/// Wraps a domain event read from the outbox, so MediatR can deliver it to handlers.
/// The domain layer stays free of MediatR.
/// </summary>
/// <param name="MessageId">The outbox message id. Handlers use it to skip a message they already handled.</param>
public sealed record DomainEventNotification<TEvent>(Guid MessageId, TEvent Event) : INotification
    where TEvent : IDomainEvent;
