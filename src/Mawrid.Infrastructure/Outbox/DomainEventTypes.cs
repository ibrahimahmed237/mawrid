using System.Collections.Frozen;
using Mawrid.Domain.Common;

namespace Mawrid.Infrastructure.Outbox;

/// <summary>Finds a domain event type by its full name, to read outbox JSON back.</summary>
public static class DomainEventTypes
{
    private static readonly FrozenDictionary<string, Type> ByName = typeof(IDomainEvent).Assembly
        .GetTypes()
        .Where(t => typeof(IDomainEvent).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
        .ToFrozenDictionary(t => t.FullName!);

    public static Type? Find(string fullName) => ByName.GetValueOrDefault(fullName);
}
