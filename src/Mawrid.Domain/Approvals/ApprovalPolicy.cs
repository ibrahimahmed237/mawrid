using Mawrid.Domain.Common;

namespace Mawrid.Domain.Approvals;

/// <summary>
/// The approval matrix for one document type (for example purchase requisitions).
/// Each rule covers an amount band and lists the approval steps, in order.
/// See DESIGN section 6.3.
/// </summary>
public sealed class ApprovalPolicy : AggregateRoot<Guid>, ITenantOwned
{
    public const int MaxRules = 10;
    public const int MaxStepsPerRule = 5;

    private readonly List<ApprovalRule> _rules = [];

    private ApprovalPolicy(Guid id, Guid organizationId, string documentType) : base(id)
    {
        OrganizationId = organizationId;
        DocumentType = documentType;
    }

    private ApprovalPolicy()
    {
        DocumentType = null!;
    }

    public Guid OrganizationId { get; private set; }

    /// <summary>Which documents this policy applies to, such as "PurchaseRequisition".</summary>
    public string DocumentType { get; private set; }

    /// <summary>Sorted by amount: the first rule whose limit covers the amount wins.</summary>
    public IReadOnlyList<ApprovalRule> Rules => _rules;

    public static Result<ApprovalPolicy> Create(Guid organizationId, string documentType, IReadOnlyList<ApprovalRule> rules)
    {
        var policy = new ApprovalPolicy(Guid.CreateVersion7(), organizationId, documentType);
        var result = policy.ReplaceRules(rules);
        return result.IsSuccess ? policy : result.Error!;
    }

    public Result ReplaceRules(IReadOnlyList<ApprovalRule> rules)
    {
        if (rules.Count is 0 or > MaxRules)
        {
            return ApprovalErrors.PolicyRuleCount;
        }

        if (rules.Any(r => r.Steps.Count is 0 or > MaxStepsPerRule))
        {
            return ApprovalErrors.PolicyStepCount;
        }

        if (rules.Count(r => r.UpToAmount is null) != 1)
        {
            return ApprovalErrors.PolicyNeedsOneCatchAll;
        }

        var limits = rules.Where(r => r.UpToAmount is not null).Select(r => r.UpToAmount!.Value).ToList();
        if (limits.Any(l => l <= 0) || limits.Distinct().Count() != limits.Count)
        {
            return ApprovalErrors.PolicyLimitsInvalid;
        }

        if (rules.SelectMany(r => r.Steps).Any(s => s.Kind == ApprovalStepKind.Group && s.GroupId is null))
        {
            return ApprovalErrors.PolicyStepNeedsGroup;
        }

        _rules.Clear();
        _rules.AddRange(rules.OrderBy(r => r.UpToAmount ?? decimal.MaxValue));
        return Result.Success();
    }

    /// <summary>The rule for an amount (total including VAT).</summary>
    public ApprovalRule RuleFor(decimal amount) =>
        _rules.First(r => r.UpToAmount is null || amount <= r.UpToAmount);
}

/// <summary>One band of the matrix. <see cref="UpToAmount"/> null means "any amount above the other bands".</summary>
public sealed record ApprovalRule(decimal? UpToAmount, IReadOnlyList<ApprovalStepTemplate> Steps);

/// <summary>One step of a rule: who must approve.</summary>
/// <param name="Name">Shown to people, for example "Department director".</param>
public sealed record ApprovalStepTemplate(string Name, ApprovalStepKind Kind, Guid? GroupId = null);

public enum ApprovalStepKind
{
    /// <summary>Users with the Approver role for the document's cost center (or for all cost centers).</summary>
    CostCenterApprovers = 1,

    /// <summary>Members of a named approver group, such as "CFO".</summary>
    Group = 2,
}
