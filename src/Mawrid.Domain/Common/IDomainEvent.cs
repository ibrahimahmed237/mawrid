namespace Mawrid.Domain.Common;

/// <summary>
/// Something that happened in the domain. Aggregates raise events; infrastructure
/// turns them into audit rows and outbox messages in the same transaction (ADR-0004).
/// </summary>
public interface IDomainEvent;
