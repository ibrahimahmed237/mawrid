using System.Net;
using System.Net.Http.Json;
using Mawrid.Api.Tests.Infrastructure;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Requisitions;

namespace Mawrid.Api.Tests.Approvals;

[Collection(ApiCollection.Name)]
public class RequisitionApprovalTests(MawridApiFactory factory)
{
    [Fact]
    public async Task A_small_requisition_needs_only_the_cost_center_manager()
    {
        var flow = await ApprovalFlow.StartAsync(factory);

        var pr = await flow.SubmittedRequisitionAsync(5_000m);
        var approval = await flow.GetApprovalAsync(pr.ApprovalRequestId!.Value);
        var inbox = await ApprovalFlow.InboxAsync(flow.Manager);
        await ApprovalFlow.ApproveAsync(flow.Manager, approval.Id, "Approved for Q4");
        var approved = await flow.GetRequisitionAsync(pr.Id);

        pr.Status.Should().Be("PendingApproval");
        pr.Number.Should().MatchRegex(@"^PR-\d{4}-\d{6}$");
        approval.Amount.Should().Be(5_750m, "the approval amount includes 15% VAT");
        approval.Steps.Should().ContainSingle().Which.Name.Should().Be("Cost center manager");
        inbox.Should().ContainSingle(i => i.Id == approval.Id && !i.AsDelegate);
        approved.Status.Should().Be("Approved");
    }

    [Fact]
    public async Task A_large_requisition_goes_through_manager_director_and_cfo_in_order()
    {
        var flow = await ApprovalFlow.StartAsync(factory);
        var pr = await flow.SubmittedRequisitionAsync(200_000m);
        var approvalId = pr.ApprovalRequestId!.Value;

        var directorTooEarly = await flow.Director.PostAsJsonAsync($"/api/v1/approvals/{approvalId}/approve", new { comment = (string?)null });
        (await ApprovalFlow.InboxAsync(flow.Director)).Should().BeEmpty();

        await ApprovalFlow.ApproveAsync(flow.Manager, approvalId);
        (await ApprovalFlow.InboxAsync(flow.Director)).Should().ContainSingle(i => i.Id == approvalId && i.StepName == "Department director");
        await ApprovalFlow.ApproveAsync(flow.Director, approvalId);
        (await flow.GetRequisitionAsync(pr.Id)).Status.Should().Be("PendingApproval");
        await ApprovalFlow.ApproveAsync(flow.Cfo, approvalId);

        await directorTooEarly.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "approval.not_an_approver");
        var approval = await flow.GetApprovalAsync(approvalId);
        approval.Status.Should().Be("Approved");
        approval.Steps.Select(s => s.Status).Should().Equal("Approved", "Approved", "Approved");
        (await flow.GetRequisitionAsync(pr.Id)).Status.Should().Be("Approved");
    }

    [Fact]
    public async Task A_rejection_rejects_the_requisition_with_the_reason()
    {
        var flow = await ApprovalFlow.StartAsync(factory);
        var pr = await flow.SubmittedRequisitionAsync(50_000m);

        var noReason = await flow.Manager.PostAsJsonAsync($"/api/v1/approvals/{pr.ApprovalRequestId}/reject", new { reason = "" });
        var reject = await flow.Manager.PostAsJsonAsync($"/api/v1/approvals/{pr.ApprovalRequestId}/reject", new { reason = "Use the spare laptops first" });
        var rejected = await flow.GetRequisitionAsync(pr.Id);

        await noReason.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "request.validation_failed");
        reject.StatusCode.Should().Be(HttpStatusCode.NoContent);
        rejected.Status.Should().Be("Rejected");
        rejected.RejectionReason.Should().Be("Use the spare laptops first");
        (await flow.GetApprovalAsync(pr.ApprovalRequestId!.Value)).Steps[1].Status.Should().Be("Skipped");
    }

    [Fact]
    public async Task A_delegate_approves_on_behalf_of_the_manager()
    {
        var flow = await ApprovalFlow.StartAsync(factory);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var delegation = await flow.Manager.PostAsJsonAsync("/api/v1/approval-delegations",
            new { toUserId = flow.ColleagueId, validFrom = today, validTo = today.AddDays(5), reason = "Annual leave" });
        var pr = await flow.SubmittedRequisitionAsync(5_000m);

        var inbox = await ApprovalFlow.InboxAsync(flow.Colleague);
        await ApprovalFlow.ApproveAsync(flow.Colleague, pr.ApprovalRequestId!.Value);
        var step = (await flow.GetApprovalAsync(pr.ApprovalRequestId!.Value)).Steps.Single();

        delegation.StatusCode.Should().Be(HttpStatusCode.Created);
        inbox.Should().ContainSingle(i => i.AsDelegate);
        step.DecidedBy.Should().Be(flow.ColleagueId);
        step.OnBehalfOf.Should().Be(flow.ManagerId);
    }

    [Fact]
    public async Task A_stale_if_match_version_is_refused()
    {
        var flow = await ApprovalFlow.StartAsync(factory);
        var pr = await flow.SubmittedRequisitionAsync(200_000m);
        var get = await flow.Manager.GetAsync($"/api/v1/approvals/{pr.ApprovalRequestId}");
        var etag = get.Headers.ETag!.Tag;
        await ApprovalFlow.ApproveAsync(flow.Manager, pr.ApprovalRequestId!.Value);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/approvals/{pr.ApprovalRequestId}/approve")
        {
            Content = JsonContent.Create(new { comment = "looks good" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        var stale = await flow.Director.SendAsync(request);

        etag.Should().StartWith("\"");
        await stale.ShouldBeProblemAsync(HttpStatusCode.PreconditionFailed, "approval.stale_version");
    }

    [Fact]
    public async Task Without_a_policy_a_requisition_cannot_be_submitted()
    {
        var flow = await ApprovalFlow.StartAsync(factory, withPolicy: false);
        var id = await flow.CreateRequisitionAsync(1_000m);

        var submit = await flow.Requester.PostAsync($"/api/v1/requisitions/{id}/submit", null);

        await submit.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "approval.no_policy");
        (await flow.GetRequisitionAsync(id)).Status.Should().Be("Draft");
    }

    [Fact]
    public async Task Cancelling_a_pending_requisition_cancels_its_approval()
    {
        var flow = await ApprovalFlow.StartAsync(factory);
        var pr = await flow.SubmittedRequisitionAsync(5_000m);

        var cancel = await flow.Requester.PostAsync($"/api/v1/requisitions/{pr.Id}/cancel", null);

        cancel.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await flow.GetApprovalAsync(pr.ApprovalRequestId!.Value)).Status.Should().Be("Cancelled");
        (await ApprovalFlow.InboxAsync(flow.Manager)).Should().BeEmpty();
    }

    [Fact]
    public async Task Only_requesters_of_the_cost_center_can_raise_requisitions()
    {
        var flow = await ApprovalFlow.StartAsync(factory);

        var response = await flow.Manager.PostAsJsonAsync("/api/v1/requisitions", new
        {
            costCenterId = flow.CostCenterId,
            title = "Chairs",
            justification = "Our chairs are broken and unsafe.",
            neededBy = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            lines = new[] { new { description = "Office chair", quantity = 2m, unit = "pcs", estimatedUnitPrice = 800m } },
        });

        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "requisition.not_requester");
    }

    [Fact]
    public async Task Requesters_see_only_their_own_requisitions_and_buyers_see_all()
    {
        var flow = await ApprovalFlow.StartAsync(factory);
        var mine = await flow.CreateRequisitionAsync(1_000m);
        var colleagues = await flow.CreateRequisitionAsync(2_000m, flow.Colleague);

        var asRequester = await (await flow.Requester.GetAsync("/api/v1/requisitions")).ShouldBeOkAsync<PagedList<RequisitionSummaryDto>>();
        var asBuyer = await (await flow.Buyer.GetAsync("/api/v1/requisitions")).ShouldBeOkAsync<PagedList<RequisitionSummaryDto>>();
        var foreign = await flow.Requester.GetAsync($"/api/v1/requisitions/{colleagues}");

        asRequester.Items.Select(i => i.Id).Should().Equal(mine);
        asBuyer.Items.Select(i => i.Id).Should().BeEquivalentTo([mine, colleagues]);
        await foreign.ShouldBeProblemAsync(HttpStatusCode.NotFound, "requisition.not_found");
    }

    [Fact]
    public async Task Numbers_are_sequential_per_organization()
    {
        var flow = await ApprovalFlow.StartAsync(factory);

        var first = await flow.GetRequisitionAsync(await flow.CreateRequisitionAsync(100m));
        var second = await flow.GetRequisitionAsync(await flow.CreateRequisitionAsync(100m));

        first.Number.Should().EndWith("-000001");
        second.Number.Should().EndWith("-000002");
    }

    [Fact]
    public async Task Buyers_choose_the_sourcing_route_within_the_direct_purchase_limit()
    {
        var flow = await ApprovalFlow.StartAsync(factory);
        var small = await flow.SubmittedRequisitionAsync(3_000m);
        var large = await flow.SubmittedRequisitionAsync(8_000m);
        await ApprovalFlow.ApproveAsync(flow.Manager, small.ApprovalRequestId!.Value);
        await ApprovalFlow.ApproveAsync(flow.Manager, large.ApprovalRequestId!.Value);

        var requesterTries = await flow.Requester.PostAsJsonAsync($"/api/v1/requisitions/{small.Id}/sourcing-route", new { route = "DirectPurchase" });
        var smallDirect = await flow.Buyer.PostAsJsonAsync($"/api/v1/requisitions/{small.Id}/sourcing-route", new { route = "DirectPurchase" });
        var largeDirect = await flow.Buyer.PostAsJsonAsync($"/api/v1/requisitions/{large.Id}/sourcing-route", new { route = "DirectPurchase" });
        var settings = await flow.Admin.PutAsJsonAsync("/api/v1/organization/settings", new
        {
            directPurchaseLimit = 10_000m,
            minimumSuppliersPerRfq = 3,
            minimumRfqWindowDays = 3,
            priceTolerancePercent = 2m,
            priceToleranceMaxAmount = 500m,
            overDeliveryPercent = 0m,
        });
        var largeAfterChange = await flow.Buyer.PostAsJsonAsync($"/api/v1/requisitions/{large.Id}/sourcing-route", new { route = "DirectPurchase" });

        await requesterTries.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "auth.forbidden");
        smallDirect.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await largeDirect.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "requisition.above_direct_limit");
        settings.StatusCode.Should().Be(HttpStatusCode.NoContent);
        largeAfterChange.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await flow.GetRequisitionAsync(large.Id)).Status.Should().Be("InSourcing");
    }

    [Fact]
    public async Task A_draft_can_be_edited_and_its_lines_replaced()
    {
        var flow = await ApprovalFlow.StartAsync(factory);
        var id = await flow.CreateRequisitionAsync(1_000m);

        var update = await flow.Requester.PutAsJsonAsync($"/api/v1/requisitions/{id}", new
        {
            costCenterId = flow.CostCenterId,
            title = "Laptops and monitors",
            justification = "Engineers need a second screen as well.",
            neededBy = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
            lines = new[]
            {
                new { description = "Laptop", quantity = 2m, unit = "pcs", estimatedUnitPrice = 4_000m },
                new { description = "Monitor 27 inch", quantity = 2m, unit = "pcs", estimatedUnitPrice = 900m },
            },
        });
        var edited = await flow.GetRequisitionAsync(id);

        update.StatusCode.Should().Be(HttpStatusCode.NoContent, await update.Content.ReadAsStringAsync());
        edited.Title.Should().Be("Laptops and monitors");
        edited.Lines.Should().HaveCount(2);
        edited.EstimatedNetTotal.Should().Be(9_800m);
    }
}
