using Mawrid.Domain.Common;

namespace Mawrid.Domain.Requisitions;

public sealed record RequisitionCreated(Guid RequisitionId, Guid OrganizationId) : IDomainEvent;

public sealed record RequisitionSubmitted(Guid RequisitionId, Guid OrganizationId, Guid ApprovalRequestId) : IDomainEvent;

public sealed record RequisitionApproved(Guid RequisitionId, Guid OrganizationId) : IDomainEvent;

public sealed record RequisitionRejected(Guid RequisitionId, Guid OrganizationId) : IDomainEvent;

public sealed record RequisitionCancelled(Guid RequisitionId, Guid OrganizationId, Guid? ApprovalRequestId) : IDomainEvent;

public sealed record RequisitionSourcingStarted(Guid RequisitionId, Guid OrganizationId, SourcingRoute Route) : IDomainEvent;

public sealed record RequisitionOrdered(Guid RequisitionId, Guid OrganizationId) : IDomainEvent;
