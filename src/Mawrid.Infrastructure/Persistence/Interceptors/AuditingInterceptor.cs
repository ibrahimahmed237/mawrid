using System.Text.Json;
using System.Text.Json.Serialization;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Domain.Common;
using Mawrid.Infrastructure.Auditing;
using Mawrid.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Mawrid.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Runs inside every <c>SaveChanges</c>, in the same transaction:
/// <list type="number">
/// <item>Blocks writes to another organization's data.</item>
/// <item>Stamps created/updated time and user on aggregate roots.</item>
/// <item>Writes one audit row per changed record of an aggregate.</item>
/// </list>
/// </summary>
public sealed class AuditingInterceptor(CurrentUserContext currentUser, IClock clock) : SaveChangesInterceptor
{
    private static readonly string[] SensitiveNameParts = ["Hash", "SecurityStamp", "ConcurrencyStamp", "RowVersion"];

    // Enums are written by name ("Buyer", not 4) so audit rows stay readable when enum values change.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

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

        context.ChangeTracker.DetectChanges();
        var now = clock.UtcNow;

        var changed = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in changed)
        {
            GuardTenant(entry);
        }

        foreach (var entry in changed)
        {
            var aggregate = FindAggregateEntry(context, entry);
            if (aggregate is null)
            {
                continue;
            }

            StampAuditFields(aggregate, now);
            context.Add(CreateAuditEntry(entry, aggregate, now));
        }
    }

    private void GuardTenant(EntityEntry entry)
    {
        if (entry.Entity is not ITenantOwned owned || entry.State == EntityState.Deleted)
        {
            return;
        }

        if (owned.OrganizationId == Guid.Empty)
        {
            throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} has no OrganizationId.");
        }

        if (currentUser.OrganizationId is { } current && owned.OrganizationId != current)
        {
            throw new InvalidOperationException(
                $"Blocked a write of {entry.Metadata.ClrType.Name} to another organization.");
        }
    }

    /// <summary>The aggregate root entry for this change: itself, or its owner for an owned type.</summary>
    private static EntityEntry? FindAggregateEntry(DbContext context, EntityEntry entry)
    {
        if (entry.Entity is IHasDomainEvents)
        {
            return entry;
        }

        var ownership = entry.Metadata.FindOwnership();
        if (ownership is null || !typeof(IHasDomainEvents).IsAssignableFrom(ownership.PrincipalEntityType.ClrType))
        {
            return null;
        }

        var ownerKey = ownership.Properties.Select(p => entry.Property(p.Name).CurrentValue).ToArray();
        return context.ChangeTracker.Entries()
            .FirstOrDefault(e => e.Metadata == ownership.PrincipalEntityType
                && e.Metadata.FindPrimaryKey()!.Properties.Select(p => e.Property(p.Name).CurrentValue).SequenceEqual(ownerKey));
    }

    private void StampAuditFields(EntityEntry aggregate, DateTimeOffset now)
    {
        if (aggregate.State == EntityState.Deleted)
        {
            return;
        }

        if (aggregate.State == EntityState.Added)
        {
            aggregate.Property(AuditFields.CreatedAtUtc).CurrentValue = now;
            aggregate.Property(AuditFields.CreatedBy).CurrentValue = currentUser.UserId;
        }

        aggregate.Property(AuditFields.UpdatedAtUtc).CurrentValue = now;
        aggregate.Property(AuditFields.UpdatedBy).CurrentValue = currentUser.UserId;

        // Marking one property (not the whole entry) as modified updates the aggregate row even when
        // only an owned child changed, so its RowVersion changes and concurrent edits are detected.
        // Setting the entry's State instead would also flip new owned children from Added to Modified.
        if (aggregate.State != EntityState.Added)
        {
            aggregate.Property(AuditFields.UpdatedAtUtc).IsModified = true;
        }
    }

    private AuditEntry CreateAuditEntry(EntityEntry entry, EntityEntry aggregate, DateTimeOffset now)
    {
        var isOwned = entry != aggregate;
        var changes = new Dictionary<string, object?>();

        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            var skip = (property.Metadata.IsShadowProperty() && !isOwned)
                || (property.Metadata.IsPrimaryKey() && isOwned)
                || IsSensitive(name);
            if (skip)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    changes[name] = property.CurrentValue;
                    break;
                case EntityState.Deleted:
                    changes[name] = property.OriginalValue;
                    break;
                case EntityState.Modified when property.IsModified && !Equals(property.OriginalValue, property.CurrentValue):
                    changes[name] = new { old = property.OriginalValue, @new = property.CurrentValue };
                    break;
            }
        }

        return new AuditEntry
        {
            OrganizationId = (aggregate.Entity as ITenantOwned)?.OrganizationId ?? currentUser.OrganizationId,
            EntityType = isOwned ? $"{aggregate.Metadata.ClrType.Name}.{entry.Metadata.ClrType.Name}" : entry.Metadata.ClrType.Name,
            EntityId = string.Join(",", aggregate.Metadata.FindPrimaryKey()!.Properties.Select(p => aggregate.Property(p.Name).CurrentValue)),
            Action = entry.State.ToString(),
            Changes = JsonSerializer.Serialize(changes, JsonOptions),
            UserId = currentUser.UserId,
            CorrelationId = currentUser.CorrelationId,
            OccurredOnUtc = now,
        };
    }

    private static bool IsSensitive(string propertyName) =>
        SensitiveNameParts.Any(part => propertyName.Contains(part, StringComparison.Ordinal));
}
