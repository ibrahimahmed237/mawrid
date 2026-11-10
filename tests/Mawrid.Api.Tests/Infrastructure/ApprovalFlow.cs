using System.Net;
using System.Net.Http.Json;
using Mawrid.Application.Approvals;
using Mawrid.Application.Requisitions;
using Mawrid.Domain.Common;

namespace Mawrid.Api.Tests.Infrastructure;

/// <summary>
/// An organization with the people of the default approval matrix:
/// a requester and a manager for cost center IT, a director and a CFO (groups), a buyer and an admin.
/// </summary>
public sealed class ApprovalFlow
{
    private ApprovalFlow(MawridApiFactory factory) => Factory = factory;

    public MawridApiFactory Factory { get; }

    public Guid OrganizationId { get; private set; }

    public Guid CostCenterId { get; private set; }

    public Guid RequesterId { get; private set; }

    public Guid ManagerId { get; private set; }

    public Guid DirectorId { get; private set; }

    public Guid CfoId { get; private set; }

    public Guid ColleagueId { get; private set; }

    public HttpClient Admin { get; private set; } = null!;

    public HttpClient Buyer { get; private set; } = null!;

    public HttpClient Requester { get; private set; } = null!;

    public HttpClient Manager { get; private set; } = null!;

    public HttpClient Director { get; private set; } = null!;

    public HttpClient Cfo { get; private set; } = null!;

    public HttpClient Colleague { get; private set; } = null!;

    public static async Task<ApprovalFlow> StartAsync(MawridApiFactory factory, bool withPolicy = true)
    {
        var flow = new ApprovalFlow(factory);
        flow.OrganizationId = await factory.CreateOrganizationAsync();
        flow.CostCenterId = await factory.CreateCostCenterAsync(flow.OrganizationId, "IT-001");

        async Task<(Guid, HttpClient)> Person(string prefix, params RoleAssignment[] roles)
        {
            var email = TestData.UniqueEmail(prefix);
            var id = await factory.CreateUserAsync(flow.OrganizationId, email, null, roles);
            return (id, await factory.CreateClientForAsync(email));
        }

        (_, flow.Admin) = await Person("admin", new RoleAssignment(Role.Admin));
        (_, flow.Buyer) = await Person("buyer", new RoleAssignment(Role.Buyer));
        (flow.RequesterId, flow.Requester) = await Person("requester", new RoleAssignment(Role.Requester, flow.CostCenterId));
        (flow.ManagerId, flow.Manager) = await Person("manager", new RoleAssignment(Role.Approver, flow.CostCenterId));
        (flow.DirectorId, flow.Director) = await Person("director", new RoleAssignment(Role.Requester));
        (flow.CfoId, flow.Cfo) = await Person("cfo", new RoleAssignment(Role.Requester));
        (flow.ColleagueId, flow.Colleague) = await Person("colleague", new RoleAssignment(Role.Requester));

        if (withPolicy)
        {
            await flow.SetDefaultPolicyAsync();
        }

        return flow;
    }

    public async Task SetDefaultPolicyAsync()
    {
        var directors = await CreateGroupAsync("Department directors", DirectorId);
        var cfo = await CreateGroupAsync("CFO", CfoId);
        var manager = new { name = "Cost center manager", kind = "CostCenterApprovers" };
        var director = new { name = "Department director", kind = "Group", groupId = (Guid?)directors };
        var chief = new { name = "CFO", kind = "Group", groupId = (Guid?)cfo };

        var response = await Admin.PutAsJsonAsync("/api/v1/approval-policies/PurchaseRequisition", new
        {
            rules = new object[]
            {
                new { upToAmount = (decimal?)10_000m, steps = new object[] { manager } },
                new { upToAmount = (decimal?)100_000m, steps = new object[] { manager, director } },
                new { upToAmount = (decimal?)null, steps = new object[] { manager, director, chief } },
            },
        });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    public async Task<Guid> CreateGroupAsync(string name, params Guid[] members)
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/approver-groups", new { name, memberUserIds = members });
        return (await response.ShouldBeOkAsync<Common.CreatedIdBody>(HttpStatusCode.Created)).Id;
    }

    /// <summary>A draft requisition. The amount is net; the approval uses net + 15% VAT.</summary>
    public async Task<Guid> CreateRequisitionAsync(decimal netAmount, HttpClient? requester = null)
    {
        var response = await (requester ?? Requester).PostAsJsonAsync("/api/v1/requisitions", new
        {
            costCenterId = CostCenterId,
            title = "Laptops for new hires",
            justification = "Three engineers start next month and need laptops.",
            neededBy = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            lines = new[] { new { description = "Laptop 14 inch", quantity = 1m, unit = "pcs", estimatedUnitPrice = netAmount } },
        });
        return (await response.ShouldBeOkAsync<Common.CreatedIdBody>(HttpStatusCode.Created)).Id;
    }

    public async Task<RequisitionDto> SubmittedRequisitionAsync(decimal netAmount)
    {
        var id = await CreateRequisitionAsync(netAmount);
        var submit = await Requester.PostAsync($"/api/v1/requisitions/{id}/submit", null);
        submit.StatusCode.Should().Be(HttpStatusCode.NoContent, await submit.Content.ReadAsStringAsync());
        return await GetRequisitionAsync(id);
    }

    public async Task<RequisitionDto> GetRequisitionAsync(Guid id, HttpClient? client = null) =>
        await (await (client ?? Requester).GetAsync($"/api/v1/requisitions/{id}")).ShouldBeOkAsync<RequisitionDto>();

    public async Task<ApprovalDto> GetApprovalAsync(Guid id) =>
        await (await Admin.GetAsync($"/api/v1/approvals/{id}")).ShouldBeOkAsync<ApprovalDto>();

    public static async Task<List<InboxItemDto>> InboxAsync(HttpClient client) =>
        await (await client.GetAsync("/api/v1/approvals/inbox")).ShouldBeOkAsync<List<InboxItemDto>>();

    public static async Task ApproveAsync(HttpClient client, Guid approvalId, string? comment = null)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/approvals/{approvalId}/approve", new { comment });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }
}
