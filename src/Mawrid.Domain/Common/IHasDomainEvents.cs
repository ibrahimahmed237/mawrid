namespace Mawrid.Domain.Common;

/// <summary>Non-generic view of an aggregate root's events, used by infrastructure.</summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
