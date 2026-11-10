using System.Collections.Concurrent;
using Mawrid.Application.Common.Events;
using Mawrid.Domain.Common;
using MediatR;

namespace Mawrid.Api.Tests.Infrastructure;

/// <summary>Collects every domain event the outbox delivers, so tests can assert on them.</summary>
public sealed class EventRecorder
{
    private readonly ConcurrentQueue<IDomainEvent> _events = new();

    public IReadOnlyCollection<IDomainEvent> Events => _events.ToArray();

    /// <summary>When set, delivery of this event type throws, to test retries.</summary>
    public Type? FailOn { get; set; }

    public void Record(IDomainEvent domainEvent)
    {
        if (domainEvent.GetType() == FailOn)
        {
            throw new InvalidOperationException($"Simulated failure for {domainEvent.GetType().Name}.");
        }

        _events.Enqueue(domainEvent);
    }
}

/// <summary>A MediatR handler for every <see cref="DomainEventNotification{TEvent}"/>; it forwards to the recorder.</summary>
public sealed class RecordingEventHandler<TNotification>(EventRecorder recorder) : INotificationHandler<TNotification>
    where TNotification : INotification
{
    public Task Handle(TNotification notification, CancellationToken cancellationToken)
    {
        var eventProperty = typeof(TNotification).GetProperty(nameof(DomainEventNotification<IDomainEvent>.Event));
        if (eventProperty?.GetValue(notification) is IDomainEvent domainEvent)
        {
            recorder.Record(domainEvent);
        }

        return Task.CompletedTask;
    }
}
