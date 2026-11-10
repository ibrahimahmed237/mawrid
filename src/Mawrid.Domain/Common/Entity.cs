namespace Mawrid.Domain.Common;

/// <summary>
/// Base class for entities. Two entities are equal when they have the same type and id.
/// </summary>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    protected Entity(TId id) => Id = id;

    /// <summary>For EF Core only.</summary>
    protected Entity() => Id = default!;

    public TId Id { get; protected set; }

    public bool Equals(Entity<TId>? other) =>
        other is not null
        && other.GetType() == GetType()
        && EqualityComparer<TId>.Default.Equals(Id, other.Id);

    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}
