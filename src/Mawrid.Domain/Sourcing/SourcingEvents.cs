using Mawrid.Domain.Common;

namespace Mawrid.Domain.Sourcing;

public sealed record RfqCreated(Guid RfqId, Guid OrganizationId) : IDomainEvent;

public sealed record RfqPublished(Guid RfqId, Guid OrganizationId, IReadOnlyList<Guid> InvitedSupplierIds, DateTimeOffset Deadline) : IDomainEvent;

public sealed record RfqAmended(Guid RfqId, Guid OrganizationId, int Revision, DateTimeOffset Deadline) : IDomainEvent;

public sealed record RfqClosed(Guid RfqId, Guid OrganizationId) : IDomainEvent;

public sealed record RfqCancelled(Guid RfqId, Guid OrganizationId, IReadOnlyList<Guid> InvitedSupplierIds) : IDomainEvent;

public sealed record RfqAwarded(Guid RfqId, Guid OrganizationId, IReadOnlyList<Guid> InvitedSupplierIds, IReadOnlyList<Guid> WinningSupplierIds) : IDomainEvent;

public sealed record QuotationSubmitted(Guid QuotationId, Guid OrganizationId, Guid RfqId, Guid SupplierId, int Version) : IDomainEvent;
