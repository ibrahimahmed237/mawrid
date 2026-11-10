using Mawrid.Domain.Common;

namespace Mawrid.Domain.Approvals;

/// <summary>
/// The approval of one document. It is created from a copy of the policy rule, so later
/// policy changes never affect it. Steps run in order; a rejection ends it.
/// </summary>
public sealed class ApprovalRequest : AggregateRoot<Guid>, ITenantOwned
{
    public const int CommentMaxLength = 500;

    private readonly List<ApprovalStep> _steps = [];

    private ApprovalRequest(Guid id, Guid organizationId, string documentType, Guid documentId, string documentTitle,
        decimal amount, Guid costCenterId, Guid requestedBy, DateTimeOffset requestedAtUtc) : base(id)
    {
        OrganizationId = organizationId;
        DocumentType = documentType;
        DocumentId = documentId;
        DocumentTitle = documentTitle;
        Amount = amount;
        CostCenterId = costCenterId;
        RequestedBy = requestedBy;
        RequestedAtUtc = requestedAtUtc;
        Status = ApprovalStatus.Pending;
    }

    private ApprovalRequest()
    {
        DocumentType = null!;
        DocumentTitle = null!;
    }

    public Guid OrganizationId { get; private set; }

    public string DocumentType { get; private set; }

    public Guid DocumentId { get; private set; }

    /// <summary>For the approver's inbox, such as "PR-2026-000012 · Laptops for new hires".</summary>
    public string DocumentTitle { get; private set; }

    /// <summary>The total including VAT that chose the rule.</summary>
    public decimal Amount { get; private set; }

    public Guid CostCenterId { get; private set; }

    public Guid RequestedBy { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public ApprovalStatus Status { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public IReadOnlyList<ApprovalStep> Steps => _steps;

    /// <summary>The step waiting for a decision, if the request is still pending.</summary>
    public ApprovalStep? CurrentStep => Status == ApprovalStatus.Pending ? _steps.FirstOrDefault(s => s.Status == ApprovalStepStatus.Pending) : null;

    /// <summary>
    /// Starts an approval. <paramref name="steps"/> are the rule's steps with their eligible
    /// approvers already resolved. The requester is removed from every step (segregation of duties).
    /// </summary>
    public static Result<ApprovalRequest> Start(
        Guid organizationId, string documentType, Guid documentId, string documentTitle, decimal amount, Guid costCenterId,
        Guid requestedBy, IReadOnlyList<(ApprovalStepTemplate Template, IReadOnlyCollection<Guid> EligibleApprovers)> steps,
        DateTimeOffset now)
    {
        if (steps.Count == 0)
        {
            return ApprovalErrors.PolicyStepCount;
        }

        var request = new ApprovalRequest(Guid.CreateVersion7(), organizationId, documentType, documentId, documentTitle,
            amount, costCenterId, requestedBy, now);

        for (var i = 0; i < steps.Count; i++)
        {
            var (template, eligible) = steps[i];
            var approvers = eligible.Where(id => id != requestedBy).Distinct().ToList();
            if (approvers.Count == 0)
            {
                return ApprovalErrors.NoApproversForStep(template.Name);
            }

            request._steps.Add(new ApprovalStep(i + 1, template.Name, approvers, i == 0 ? ApprovalStepStatus.Pending : ApprovalStepStatus.Waiting));
        }

        request.Raise(new ApprovalRequestStarted(request.Id, organizationId, documentType, documentId));
        return request;
    }

    /// <summary>Who may decide the current step: an eligible approver, or someone acting for one through an active delegation.</summary>
    public Guid? ResolveOnBehalfOf(Guid userId, IEnumerable<ApprovalDelegation> activeDelegationsToUser, out bool allowed)
    {
        allowed = false;
        var step = CurrentStep;
        if (step is null || userId == RequestedBy)
        {
            return null;
        }

        if (step.EligibleApproverIds.Contains(userId))
        {
            allowed = true;
            return null;
        }

        var delegator = activeDelegationsToUser
            .Where(d => d.ToUserId == userId && d.FromUserId != RequestedBy)
            .Select(d => (Guid?)d.FromUserId)
            .FirstOrDefault(from => step.EligibleApproverIds.Contains(from!.Value));
        allowed = delegator is not null;
        return delegator;
    }

    public Result Approve(Guid userId, IEnumerable<ApprovalDelegation> activeDelegationsToUser, string? comment, DateTimeOffset now) =>
        Decide(userId, activeDelegationsToUser, comment, now, approve: true);

    public Result Reject(Guid userId, IEnumerable<ApprovalDelegation> activeDelegationsToUser, string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return ApprovalErrors.RejectReasonRequired;
        }

        return Decide(userId, activeDelegationsToUser, reason, now, approve: false);
    }

    /// <summary>The document was withdrawn; stop the approval.</summary>
    public void Cancel(DateTimeOffset now)
    {
        if (Status != ApprovalStatus.Pending)
        {
            return;
        }

        Status = ApprovalStatus.Cancelled;
        CompletedAtUtc = now;
        Raise(new ApprovalRequestCancelled(Id, OrganizationId, DocumentType, DocumentId));
    }

    private Result Decide(Guid userId, IEnumerable<ApprovalDelegation> delegations, string? comment, DateTimeOffset now, bool approve)
    {
        if (Status != ApprovalStatus.Pending)
        {
            return ApprovalErrors.NotPending;
        }

        if (userId == RequestedBy)
        {
            return ApprovalErrors.CannotApproveOwnRequest;
        }

        var onBehalfOf = ResolveOnBehalfOf(userId, delegations, out var allowed);
        if (!allowed)
        {
            return ApprovalErrors.NotAnApprover;
        }

        comment = comment?.Trim();
        if (comment?.Length > CommentMaxLength)
        {
            return ApprovalErrors.CommentTooLong;
        }

        var step = CurrentStep!;
        step.Decide(approve, userId, onBehalfOf, comment, now);

        if (!approve)
        {
            foreach (var later in _steps.Where(s => s.Status == ApprovalStepStatus.Waiting))
            {
                later.Skip();
            }

            Status = ApprovalStatus.Rejected;
            CompletedAtUtc = now;
            Raise(new ApprovalRequestRejected(Id, OrganizationId, DocumentType, DocumentId, comment ?? string.Empty));
            return Result.Success();
        }

        var next = _steps.FirstOrDefault(s => s.Status == ApprovalStepStatus.Waiting);
        if (next is not null)
        {
            next.Activate();
            Raise(new ApprovalStepActivated(Id, OrganizationId, next.Order));
            return Result.Success();
        }

        Status = ApprovalStatus.Approved;
        CompletedAtUtc = now;
        Raise(new ApprovalRequestApproved(Id, OrganizationId, DocumentType, DocumentId));
        return Result.Success();
    }
}

public sealed class ApprovalStep
{
    internal ApprovalStep(int order, string name, List<Guid> eligibleApproverIds, ApprovalStepStatus status)
    {
        Order = order;
        Name = name;
        EligibleApproverIds = eligibleApproverIds;
        Status = status;
    }

    private ApprovalStep()
    {
        Name = null!;
        EligibleApproverIds = [];
    }

    public int Order { get; private set; }

    public string Name { get; private set; }

    /// <summary>Copied when the request starts, so later role or group changes do not affect it.</summary>
    public List<Guid> EligibleApproverIds { get; private set; }

    public ApprovalStepStatus Status { get; private set; }

    public Guid? DecidedBy { get; private set; }

    /// <summary>Set when a delegate decided for an eligible approver.</summary>
    public Guid? OnBehalfOf { get; private set; }

    public DateTimeOffset? DecidedAtUtc { get; private set; }

    public string? Comment { get; private set; }

    internal void Activate() => Status = ApprovalStepStatus.Pending;

    internal void Skip() => Status = ApprovalStepStatus.Skipped;

    internal void Decide(bool approve, Guid userId, Guid? onBehalfOf, string? comment, DateTimeOffset now)
    {
        Status = approve ? ApprovalStepStatus.Approved : ApprovalStepStatus.Rejected;
        DecidedBy = userId;
        OnBehalfOf = onBehalfOf;
        DecidedAtUtc = now;
        Comment = comment;
    }
}

public enum ApprovalStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
}

public enum ApprovalStepStatus
{
    /// <summary>An earlier step is not decided yet.</summary>
    Waiting = 1,

    /// <summary>The current step.</summary>
    Pending = 2,
    Approved = 3,
    Rejected = 4,

    /// <summary>Not needed, because an earlier step rejected.</summary>
    Skipped = 5,
}
