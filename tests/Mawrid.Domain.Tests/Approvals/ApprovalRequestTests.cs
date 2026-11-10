using Mawrid.Domain.Approvals;

namespace Mawrid.Domain.Tests.Approvals;

public class ApprovalRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 10);
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Director = Guid.NewGuid();
    private static readonly Guid Deputy = Guid.NewGuid();

    private static ApprovalRequest TwoStepRequest(params Guid[] extraManagers) => ApprovalRequest.Start(
        Org, "PurchaseRequisition", Guid.NewGuid(), "PR-2026-000001 · Laptops", 50_000m, Guid.NewGuid(), Requester,
        [
            (new ApprovalStepTemplate("Cost center manager", ApprovalStepKind.CostCenterApprovers), [Manager, .. extraManagers]),
            (new ApprovalStepTemplate("Director", ApprovalStepKind.Group, Guid.NewGuid()), [Director]),
        ],
        Now).Value;

    [Fact]
    public void Steps_run_in_order_and_the_last_approval_completes_the_request()
    {
        var request = TwoStepRequest();

        request.CurrentStep!.Name.Should().Be("Cost center manager");
        request.Approve(Director, [], null, Now).Error.Should().Be(ApprovalErrors.NotAnApprover, "the director's step is not active yet");

        request.Approve(Manager, [], "OK", Now).IsSuccess.Should().BeTrue();
        request.CurrentStep!.Name.Should().Be("Director");

        request.Approve(Director, [], null, Now.AddHours(1)).IsSuccess.Should().BeTrue();
        request.Status.Should().Be(ApprovalStatus.Approved);
        request.CompletedAtUtc.Should().Be(Now.AddHours(1));
        request.DomainEvents.OfType<ApprovalRequestApproved>().Should().ContainSingle();
    }

    [Fact]
    public void A_rejection_ends_the_request_and_skips_later_steps()
    {
        var request = TwoStepRequest();

        request.Reject(Manager, [], "Over budget", Now).IsSuccess.Should().BeTrue();

        request.Status.Should().Be(ApprovalStatus.Rejected);
        request.Steps[1].Status.Should().Be(ApprovalStepStatus.Skipped);
        request.DomainEvents.OfType<ApprovalRequestRejected>().Single().Reason.Should().Be("Over budget");
        request.Approve(Director, [], null, Now).Error.Should().Be(ApprovalErrors.NotPending);
    }

    [Fact]
    public void Rejecting_needs_a_reason()
    {
        TwoStepRequest().Reject(Manager, [], " ", Now).Error.Should().Be(ApprovalErrors.RejectReasonRequired);
    }

    [Fact]
    public void The_requester_is_never_an_approver()
    {
        var request = TwoStepRequest(Requester);

        request.Steps[0].EligibleApproverIds.Should().NotContain(Requester);
        request.Approve(Requester, [], null, Now).Error.Should().Be(ApprovalErrors.CannotApproveOwnRequest);
    }

    [Fact]
    public void A_step_with_only_the_requester_as_approver_cannot_start()
    {
        var result = ApprovalRequest.Start(Org, "PurchaseRequisition", Guid.NewGuid(), "PR", 10m, Guid.NewGuid(), Requester,
            [(new ApprovalStepTemplate("Manager", ApprovalStepKind.CostCenterApprovers), [Requester])], Now);

        result.Error!.Code.Should().Be("approval.no_approvers");
    }

    [Fact]
    public void An_active_delegate_can_decide_for_the_approver()
    {
        var request = TwoStepRequest();
        var delegation = ApprovalDelegation.Create(Org, Manager, Deputy, Today, Today.AddDays(7), Today, "Annual leave").Value;

        request.Approve(Deputy, [delegation], null, Now).IsSuccess.Should().BeTrue();

        request.Steps[0].DecidedBy.Should().Be(Deputy);
        request.Steps[0].OnBehalfOf.Should().Be(Manager);
    }

    [Fact]
    public void A_delegation_from_someone_who_is_not_on_the_step_gives_no_rights()
    {
        var request = TwoStepRequest();
        var delegation = ApprovalDelegation.Create(Org, Director, Deputy, Today, Today.AddDays(7), Today, null).Value;

        request.Approve(Deputy, [delegation], null, Now).Error.Should().Be(ApprovalErrors.NotAnApprover);
    }

    [Fact]
    public void Cancel_stops_a_pending_request_only()
    {
        var request = TwoStepRequest();

        request.Cancel(Now);
        request.Cancel(Now.AddMinutes(1));

        request.Status.Should().Be(ApprovalStatus.Cancelled);
        request.DomainEvents.OfType<ApprovalRequestCancelled>().Should().ContainSingle();
    }
}
