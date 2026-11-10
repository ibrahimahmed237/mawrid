using Mawrid.Domain.Common;

namespace Mawrid.Domain.Approvals;

public sealed record ApprovalRequestStarted(Guid ApprovalRequestId, Guid OrganizationId, string DocumentType, Guid DocumentId) : IDomainEvent;

public sealed record ApprovalStepActivated(Guid ApprovalRequestId, Guid OrganizationId, int StepOrder) : IDomainEvent;

public sealed record ApprovalRequestApproved(Guid ApprovalRequestId, Guid OrganizationId, string DocumentType, Guid DocumentId) : IDomainEvent;

public sealed record ApprovalRequestRejected(Guid ApprovalRequestId, Guid OrganizationId, string DocumentType, Guid DocumentId, string Reason) : IDomainEvent;

public sealed record ApprovalRequestCancelled(Guid ApprovalRequestId, Guid OrganizationId, string DocumentType, Guid DocumentId) : IDomainEvent;
