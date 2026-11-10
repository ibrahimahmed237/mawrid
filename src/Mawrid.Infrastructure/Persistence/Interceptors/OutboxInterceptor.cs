using System.Text.Json;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Domain.Common;
using Mawrid.Domain.Identity;
using Mawrid.Infrastructure.Identity;
using Mawrid.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Mawrid.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Moves domain events from aggregates into the outbox table, in the same
/// transaction as the change (ADR-0004). Nothing is sent from here.
/// </summary>
public sealed class OutboxInterceptor(CurrentUserContext currentUser, IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.UtcNow;
        var aggregates = context.ChangeTracker.Entries()
            .Select(e => e.Entity)
            .OfType<IHasDomainEvents>()
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            var organizationId = aggregate switch
            {
                ITenantOwned owned => owned.OrganizationId,
                Organization organization => organization.Id,
                _ => currentUser.OrganizationId,
            };

            foreach (var domainEvent in aggregate.DomainEvents)
            {
                context.Add(new OutboxMessage
                {
                    Id = Guid.CreateVersion7(),
                    Type = domainEvent.GetType().FullName!,
                    Content = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), JsonSerializerOptions.Web),
                    OrganizationId = organizationId,
                    CorrelationId = currentUser.CorrelationId,
                    OccurredOnUtc = now,
                    NextAttemptAtUtc = now,
                });
            }

            aggregate.ClearDomainEvents();
        }
    }
}
