using Mawrid.Domain.Common;

namespace Mawrid.Domain.Approvals;

/// <summary>
/// An approver hands their approval rights to someone else for a period, for example
/// during leave. Decisions are recorded as "by X on behalf of Y".
/// </summary>
public sealed class ApprovalDelegation : AggregateRoot<Guid>, ITenantOwned
{
    public const int MaxDays = 90;

    private ApprovalDelegation(Guid id, Guid organizationId, Guid fromUserId, Guid toUserId, DateOnly validFrom, DateOnly validTo, string? reason)
        : base(id)
    {
        OrganizationId = organizationId;
        FromUserId = fromUserId;
        ToUserId = toUserId;
        ValidFrom = validFrom;
        ValidTo = validTo;
        Reason = reason;
    }

    private ApprovalDelegation()
    {
    }

    public Guid OrganizationId { get; private set; }

    public Guid FromUserId { get; private set; }

    public Guid ToUserId { get; private set; }

    /// <summary>First day the delegate can act (business date, inclusive).</summary>
    public DateOnly ValidFrom { get; private set; }

    /// <summary>Last day the delegate can act (inclusive).</summary>
    public DateOnly ValidTo { get; private set; }

    public string? Reason { get; private set; }

    public bool IsRevoked { get; private set; }

    public static Result<ApprovalDelegation> Create(
        Guid organizationId, Guid fromUserId, Guid toUserId, DateOnly validFrom, DateOnly validTo, DateOnly today, string? reason)
    {
        if (fromUserId == toUserId)
        {
            return ApprovalErrors.DelegateToSelf;
        }

        if (validFrom < today || validTo < validFrom || validTo.DayNumber - validFrom.DayNumber + 1 > MaxDays)
        {
            return ApprovalErrors.DelegationPeriodInvalid;
        }

        return new ApprovalDelegation(Guid.CreateVersion7(), organizationId, fromUserId, toUserId, validFrom, validTo, reason?.Trim());
    }

    public bool IsActiveOn(DateOnly day) => !IsRevoked && day >= ValidFrom && day <= ValidTo;

    public void Revoke() => IsRevoked = true;
}
