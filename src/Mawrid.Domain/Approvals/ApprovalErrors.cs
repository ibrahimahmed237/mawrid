using Mawrid.Domain.Common;

namespace Mawrid.Domain.Approvals;

public static class ApprovalErrors
{
    public static readonly Error PolicyRuleCount =
        Error.Validation("approval_policy.rule_count", $"A policy needs 1 to {ApprovalPolicy.MaxRules} rules.");

    public static readonly Error PolicyStepCount =
        Error.Validation("approval_policy.step_count", $"Each rule needs 1 to {ApprovalPolicy.MaxStepsPerRule} steps.");

    public static readonly Error PolicyNeedsOneCatchAll =
        Error.Validation("approval_policy.catch_all", "Exactly one rule must have no amount limit (it covers everything above the other rules).");

    public static readonly Error PolicyLimitsInvalid =
        Error.Validation("approval_policy.limits_invalid", "Amount limits must be positive and different from each other.");

    public static readonly Error PolicyStepNeedsGroup =
        Error.Validation("approval_policy.step_needs_group", "A group step needs a group.");

    public static readonly Error PolicyNotFound =
        Error.RuleViolation("approval.no_policy", "No approval policy is set up for this document type. Ask an admin to create one.");

    public static readonly Error GroupNameInvalid =
        Error.Validation("approver_group.name_invalid", "The group name must be 2 to 100 characters.");

    public static readonly Error GroupMemberCount =
        Error.Validation("approver_group.member_count", $"A group needs 1 to {ApproverGroup.MaxMembers} members.");

    public static readonly Error GroupNotFound =
        Error.NotFound("approver_group.not_found", "The approver group was not found.");

    public static readonly Error DelegateToSelf =
        Error.Validation("delegation.to_self", "You cannot delegate to yourself.");

    public static readonly Error DelegationPeriodInvalid =
        Error.Validation("delegation.period_invalid", $"The period must start today or later, end on or after the start, and last at most {ApprovalDelegation.MaxDays} days.");

    public static readonly Error DelegationNotFound =
        Error.NotFound("delegation.not_found", "The delegation was not found.");

    public static readonly Error NotFound =
        Error.NotFound("approval.not_found", "The approval request was not found.");

    public static readonly Error NotPending =
        Error.RuleViolation("approval.not_pending", "This approval is already decided.");

    public static readonly Error CannotApproveOwnRequest =
        Error.Forbidden("approval.own_request", "You cannot approve or reject your own request.");

    public static readonly Error NotAnApprover =
        Error.Forbidden("approval.not_an_approver", "You are not an approver for the current step.");

    public static readonly Error RejectReasonRequired =
        Error.Validation("approval.reason_required", "A reason is required to reject.");

    public static readonly Error CommentTooLong =
        Error.Validation("approval.comment_too_long", $"The comment can be at most {ApprovalRequest.CommentMaxLength} characters.");

    public static readonly Error StaleVersion =
        Error.Precondition("approval.stale_version", "The approval changed since you loaded it. Reload and try again.");

    public static Error NoApproversForStep(string stepName) =>
        Error.RuleViolation("approval.no_approvers", $"Nobody can approve the step '{stepName}'. Ask an admin to assign approvers.");
}
