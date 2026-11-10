using Mawrid.Domain.Approvals;

namespace Mawrid.Domain.Tests.Approvals;

public class ApprovalPolicyTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid DirectorsGroup = Guid.NewGuid();
    private static readonly Guid CfoGroup = Guid.NewGuid();

    private static readonly ApprovalStepTemplate Manager = new("Cost center manager", ApprovalStepKind.CostCenterApprovers);
    private static readonly ApprovalStepTemplate Director = new("Director", ApprovalStepKind.Group, DirectorsGroup);
    private static readonly ApprovalStepTemplate Cfo = new("CFO", ApprovalStepKind.Group, CfoGroup);

    private static ApprovalPolicy DefaultMatrix() => ApprovalPolicy.Create(Org, "PurchaseRequisition",
    [
        new ApprovalRule(null, [Manager, Director, Cfo]),
        new ApprovalRule(10_000m, [Manager]),
        new ApprovalRule(100_000m, [Manager, Director]),
    ]).Value;

    [Theory]
    [InlineData(500, 1)]
    [InlineData(10_000, 1)]
    [InlineData(10_000.01, 2)]
    [InlineData(100_000, 2)]
    [InlineData(1_000_000, 3)]
    public void The_amount_picks_the_band(decimal amount, int expectedSteps)
    {
        DefaultMatrix().RuleFor(amount).Steps.Should().HaveCount(expectedSteps);
    }

    [Fact]
    public void Rules_are_kept_sorted_by_limit_with_the_catch_all_last()
    {
        DefaultMatrix().Rules.Select(r => r.UpToAmount).Should().Equal(10_000m, 100_000m, null);
    }

    [Fact]
    public void Exactly_one_catch_all_rule_is_required()
    {
        ApprovalPolicy.Create(Org, "PurchaseRequisition", [new ApprovalRule(10_000m, [Manager])])
            .Error.Should().Be(ApprovalErrors.PolicyNeedsOneCatchAll);
        ApprovalPolicy.Create(Org, "PurchaseRequisition", [new ApprovalRule(null, [Manager]), new ApprovalRule(null, [Director])])
            .Error.Should().Be(ApprovalErrors.PolicyNeedsOneCatchAll);
    }

    [Fact]
    public void Limits_must_be_positive_and_unique()
    {
        ApprovalPolicy.Create(Org, "PurchaseRequisition",
            [new ApprovalRule(5_000m, [Manager]), new ApprovalRule(5_000m, [Director]), new ApprovalRule(null, [Cfo])])
            .Error.Should().Be(ApprovalErrors.PolicyLimitsInvalid);
    }

    [Fact]
    public void A_group_step_needs_a_group()
    {
        ApprovalPolicy.Create(Org, "PurchaseRequisition", [new ApprovalRule(null, [new ApprovalStepTemplate("CFO", ApprovalStepKind.Group)])])
            .Error.Should().Be(ApprovalErrors.PolicyStepNeedsGroup);
    }

    [Fact]
    public void Delegation_rules()
    {
        var today = new DateOnly(2026, 11, 10);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        ApprovalDelegation.Create(Org, a, a, today, today, today, null).Error.Should().Be(ApprovalErrors.DelegateToSelf);
        ApprovalDelegation.Create(Org, a, b, today.AddDays(-1), today, today, null).Error.Should().Be(ApprovalErrors.DelegationPeriodInvalid);
        ApprovalDelegation.Create(Org, a, b, today, today.AddDays(90), today, null).Error.Should().Be(ApprovalErrors.DelegationPeriodInvalid);

        var delegation = ApprovalDelegation.Create(Org, a, b, today, today.AddDays(89), today, null).Value;
        delegation.IsActiveOn(today.AddDays(89)).Should().BeTrue();
        delegation.IsActiveOn(today.AddDays(90)).Should().BeFalse();
        delegation.Revoke();
        delegation.IsActiveOn(today).Should().BeFalse();
    }
}
