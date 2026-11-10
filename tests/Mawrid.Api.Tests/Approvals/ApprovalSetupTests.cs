using System.Net;
using System.Net.Http.Json;
using Mawrid.Api.Tests.Infrastructure;
using Mawrid.Application.Approvals;
using Mawrid.Domain.Common;

namespace Mawrid.Api.Tests.Approvals;

[Collection(ApiCollection.Name)]
public class ApprovalSetupTests(MawridApiFactory factory)
{
    [Fact]
    public async Task A_policy_needs_exactly_one_catch_all_rule()
    {
        var flow = await ApprovalFlow.StartAsync(factory, withPolicy: false);
        var step = new { name = "Manager", kind = "CostCenterApprovers" };

        var response = await flow.Admin.PutAsJsonAsync("/api/v1/approval-policies/PurchaseRequisition", new
        {
            rules = new[] { new { upToAmount = (decimal?)5_000m, steps = new[] { step } } },
        });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "approval_policy.catch_all");
    }

    [Fact]
    public async Task A_policy_cannot_point_at_an_unknown_group()
    {
        var flow = await ApprovalFlow.StartAsync(factory, withPolicy: false);

        var response = await flow.Admin.PutAsJsonAsync("/api/v1/approval-policies/PurchaseRequisition", new
        {
            rules = new[] { new { upToAmount = (decimal?)null, steps = new[] { new { name = "CFO", kind = "Group", groupId = (Guid?)Guid.NewGuid() } } } },
        });

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "approver_group.not_found");
    }

    [Fact]
    public async Task Policies_can_be_read_back_and_replaced()
    {
        var flow = await ApprovalFlow.StartAsync(factory);

        var policies = await (await flow.Admin.GetAsync("/api/v1/approval-policies")).ShouldBeOkAsync<List<ApprovalPolicyDto>>();

        policies.Should().ContainSingle().Which.Rules.Select(r => r.Steps.Count).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Group_members_must_be_active_employees()
    {
        var flow = await ApprovalFlow.StartAsync(factory, withPolicy: false);
        var supplierUser = await factory.CreateUserAsync(flow.OrganizationId, TestData.UniqueEmail("supplier"), Guid.NewGuid(), new RoleAssignment(Role.Supplier));

        var response = await flow.Admin.PostAsJsonAsync("/api/v1/approver-groups", new { name = "Bad group", memberUserIds = new[] { supplierUser } });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "approver_group.members_invalid");
    }

    [Fact]
    public async Task If_the_only_approver_is_the_requester_the_requisition_cannot_be_submitted()
    {
        var flow = await ApprovalFlow.StartAsync(factory);
        var email = TestData.UniqueEmail("both");
        await factory.CreateUserAsync(flow.OrganizationId, email, null,
            new RoleAssignment(Role.Requester, flow.CostCenterId), new RoleAssignment(Role.Approver, flow.CostCenterId));
        var both = await factory.CreateClientForAsync(email);
        await flow.Admin.PostAsync($"/api/v1/users/{flow.ManagerId}/deactivate", null);
        var id = await flow.CreateRequisitionAsync(1_000m, both);

        var submit = await both.PostAsync($"/api/v1/requisitions/{id}/submit", null);

        await submit.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "approval.no_approvers");
    }

    [Fact]
    public async Task Delegations_can_be_revoked_only_by_the_person_who_gave_them()
    {
        var flow = await ApprovalFlow.StartAsync(factory);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var created = await flow.Manager.PostAsJsonAsync("/api/v1/approval-delegations",
            new { toUserId = flow.ColleagueId, validFrom = today, validTo = today.AddDays(2) });
        var id = (await created.ShouldBeOkAsync<Common.CreatedIdBody>(HttpStatusCode.Created)).Id;

        var byColleague = await flow.Colleague.PostAsync($"/api/v1/approval-delegations/{id}/revoke", null);
        var byManager = await flow.Manager.PostAsync($"/api/v1/approval-delegations/{id}/revoke", null);
        var mine = await (await flow.Manager.GetAsync("/api/v1/approval-delegations")).ShouldBeOkAsync<List<DelegationDto>>();

        await byColleague.ShouldBeProblemAsync(HttpStatusCode.NotFound, "delegation.not_found");
        byManager.StatusCode.Should().Be(HttpStatusCode.NoContent);
        mine.Should().ContainSingle().Which.IsRevoked.Should().BeTrue();
    }
}
