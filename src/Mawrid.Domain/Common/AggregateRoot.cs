namespace Mawrid.Domain.Common;

/// <summary>
/// Base class for aggregate roots. An aggregate root is the only entry point for
/// changing the entities inside it, and it collects the domain events it raises.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id) : base(id)
    {
    }

    /// <summary>For EF Core only.</summary>
    protected AggregateRoot()
    {
    }

    /// <summary>
    /// Concurrency token. SQL Server updates it on every write, so two users
    /// saving the same version get a conflict instead of a lost update.
    /// </summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
