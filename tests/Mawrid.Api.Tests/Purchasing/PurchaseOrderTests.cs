using System.Net;
using System.Net.Http.Json;
using Mawrid.Api.Tests.Infrastructure;
using Mawrid.Application.Approvals;
using Mawrid.Application.Purchasing;
using Mawrid.Application.Requisitions;

namespace Mawrid.Api.Tests.Purchasing;

[Collection(ApiCollection.Name)]
public sealed class PurchaseOrderTests(MawridApiFactory factory)
{
    [Fact]
    public async Task From_award_to_approval_sending_acknowledgement_and_receipts()
    {
        var flow = await PurchasingFlow.StartAsync(factory);
        var id = await flow.DraftPurchaseOrderAsync();

        // The buyer sets the terms, using the ETag from GET.
        var draft = await flow.Buyer.GetAsync($"/api/v1/purchase-orders/{id}");
        var etag = draft.Headers.ETag!.Tag.Trim('"');
        var terms = await PurchasingFlow.SendWithVersionAsync(flow.Buyer, HttpMethod.Put, $"/api/v1/purchase-orders/{id}/terms",
            new { paymentTermsDays = 45, deliveryAddress = "Riyadh warehouse, gate 3" }, etag);
        terms.StatusCode.Should().Be(HttpStatusCode.NoContent, await terms.Content.ReadAsStringAsync());

        await flow.ApprovedAsync(id);
        (await flow.GetAsync(id)).Status.Should().Be("Approved");

        (await flow.Buyer.PostAsync($"/api/v1/purchase-orders/{id}/send", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var supplierView = await (await flow.Supplier.User.GetAsync($"/api/v1/supplier/purchase-orders/{id}")).ShouldBeOkAsync<SupplierPurchaseOrderDto>();
        supplierView.Status.Should().Be("Sent");
        supplierView.PaymentTermsDays.Should().Be(45);
        supplierView.Total.Should().Be(50_600m);

        (await flow.Supplier.User.PostAsync($"/api/v1/supplier/purchase-orders/{id}/acknowledge", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // First delivery: 6 laptops accepted, 1 rejected as damaged.
        var first = await flow.ReceiveAsync(id, new { lineNumber = 1, acceptedQuantity = 6m, rejectedQuantity = 1m, rejectionReason = "Broken screen" });
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var partly = await flow.GetAsync(id);
        partly.Status.Should().Be("PartiallyReceived");
        partly.Lines[0].AcceptedQuantity.Should().Be(6m);
        partly.Lines[0].RejectedQuantity.Should().Be(1m);

        // Second delivery completes the order.
        (await flow.ReceiveAsync(id, new { lineNumber = 1, acceptedQuantity = 4m }, new { lineNumber = 2, acceptedQuantity = 10m }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var received = await flow.GetAsync(id);
        received.Status.Should().Be("Received");
        received.Receipts.Should().HaveCount(2);

        // Nothing is invoiced yet, so it cannot close.
        var close = await flow.Buyer.PostAsJsonAsync($"/api/v1/purchase-orders/{id}/close", new { reason = (string?)null });
        await close.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "purchase_order.cannot_close");
    }

    [Fact]
    public async Task A_stale_if_match_gives_412()
    {
        var flow = await PurchasingFlow.StartAsync(factory);
        var id = await flow.DraftPurchaseOrderAsync();
        var oldVersion = (await flow.GetAsync(id)).Version;
        (await flow.Buyer.PutAsJsonAsync($"/api/v1/purchase-orders/{id}/terms", new { paymentTermsDays = 60 })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var submit = await PurchasingFlow.SendWithVersionAsync(flow.Buyer, HttpMethod.Post, $"/api/v1/purchase-orders/{id}/submit", null, oldVersion);

        await submit.ShouldBeProblemAsync(HttpStatusCode.PreconditionFailed, "purchase_order.stale_version");
    }

    [Fact]
    public async Task A_rejected_po_returns_to_draft_and_can_be_submitted_again()
    {
        var flow = await PurchasingFlow.StartAsync(factory);
        var id = await flow.DraftPurchaseOrderAsync();
        (await flow.Buyer.PostAsync($"/api/v1/purchase-orders/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var approvalId = (await flow.GetAsync(id)).ApprovalRequestId!.Value;
        (await flow.Manager.PostAsJsonAsync($"/api/v1/approvals/{approvalId}/reject", new { reason = "Payment terms must be 60 days" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var rejected = await flow.GetAsync(id);
        rejected.Status.Should().Be("Draft");
        rejected.RejectionReason.Should().Be("Payment terms must be 60 days");

        (await flow.Buyer.PutAsJsonAsync($"/api/v1/purchase-orders/{id}/terms", new { paymentTermsDays = 60 })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await flow.Buyer.PostAsync($"/api/v1/purchase-orders/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await flow.GetAsync(id)).Status.Should().Be("PendingApproval");
    }

    [Fact]
    public async Task Cancelling_a_pending_po_cancels_its_approval()
    {
        var flow = await PurchasingFlow.StartAsync(factory);
        var id = await flow.DraftPurchaseOrderAsync();
        (await flow.Buyer.PostAsync($"/api/v1/purchase-orders/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var approvalId = (await flow.GetAsync(id)).ApprovalRequestId!.Value;

        (await flow.Buyer.PostAsJsonAsync($"/api/v1/purchase-orders/{id}/cancel", new { reason = "Budget frozen" })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await flow.GetAsync(id)).Status.Should().Be("Cancelled");
        (await flow.Sourcing.Approvals.GetApprovalAsync(approvalId)).Status.Should().Be("Cancelled");
        (await ApprovalFlow.InboxAsync(flow.Manager)).Should().NotContain(i => i.Id == approvalId);
    }

    [Fact]
    public async Task A_po_for_a_blocked_supplier_cannot_be_submitted()
    {
        var flow = await PurchasingFlow.StartAsync(factory);
        var id = await flow.DraftPurchaseOrderAsync();
        (await flow.Sourcing.Approvals.Admin.PostAsJsonAsync($"/api/v1/suppliers/{flow.Supplier.Id}/block", new { reason = "Failed quality audit" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var submit = await flow.Buyer.PostAsync($"/api/v1/purchase-orders/{id}/submit", null);

        await submit.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "purchase_order.supplier_not_awardable");
    }

    [Fact]
    public async Task Suppliers_see_only_their_own_sent_pos_without_internal_fields()
    {
        var flow = await PurchasingFlow.StartAsync(factory);
        var draftId = await flow.DraftPurchaseOrderAsync();
        var otherSupplier = flow.Sourcing.ApprovedSuppliers[1].User;

        (await flow.Supplier.User.GetAsync($"/api/v1/supplier/purchase-orders/{draftId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await flow.ApprovedAsync(draftId);
        (await flow.Buyer.PostAsync($"/api/v1/purchase-orders/{draftId}/send", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var json = await (await flow.Supplier.User.GetAsync($"/api/v1/supplier/purchase-orders/{draftId}")).Content.ReadAsStringAsync();
        json.Should().NotContain("approvalRequestId").And.NotContain("costCenterId").And.NotContain("rejectionReason");

        (await otherSupplier.GetAsync($"/api/v1/supplier/purchase-orders/{draftId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await (await otherSupplier.PostAsync($"/api/v1/supplier/purchase-orders/{draftId}/acknowledge", null))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "purchase_order.not_found");
        var list = await (await otherSupplier.GetAsync("/api/v1/supplier/purchase-orders")).ShouldBeOkAsync<PageBody>();
        list.Items.Should().NotContain(p => p.Id == draftId);
    }

    [Fact]
    public async Task Another_organization_cannot_see_or_change_the_po()
    {
        var flow = await PurchasingFlow.StartAsync(factory);
        var id = await flow.DraftPurchaseOrderAsync();
        var other = await ApprovalFlow.StartAsync(factory, withPolicy: false);

        (await other.Buyer.GetAsync($"/api/v1/purchase-orders/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await (await other.Buyer.PostAsync($"/api/v1/purchase-orders/{id}/submit", null))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "purchase_order.not_found");
    }

    [Fact]
    public async Task Receipts_need_the_receiver_role_and_respect_the_ordered_quantity()
    {
        var flow = await PurchasingFlow.StartAsync(factory);
        var id = await flow.SentPurchaseOrderAsync();

        var byBuyer = await flow.Buyer.PostAsJsonAsync($"/api/v1/purchase-orders/{id}/receipts",
            new { lines = new[] { new { lineNumber = 1, acceptedQuantity = 1m } } });
        await byBuyer.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "auth.forbidden");

        var tooMany = await flow.ReceiveAsync(id, new { lineNumber = 1, acceptedQuantity = 11m });
        await tooMany.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "receipt.over_delivery");
    }

    [Fact]
    public async Task A_partly_received_po_can_be_short_closed_with_a_reason()
    {
        var flow = await PurchasingFlow.StartAsync(factory);
        var id = await flow.SentPurchaseOrderAsync();
        (await flow.ReceiveAsync(id, new { lineNumber = 1, acceptedQuantity = 10m })).StatusCode.Should().Be(HttpStatusCode.Created);

        var noReason = await flow.Buyer.PostAsJsonAsync($"/api/v1/purchase-orders/{id}/close", new { reason = (string?)null });
        await noReason.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "purchase_order.short_close_reason");

        (await flow.Buyer.PostAsJsonAsync($"/api/v1/purchase-orders/{id}/close", new { reason = "Docks are discontinued" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var closed = await flow.GetAsync(id);
        closed.Status.Should().Be("Closed");
        closed.ClosedReason.Should().Be("Docks are discontinued");
    }

    [Fact]
    public async Task A_small_requisition_can_be_bought_directly()
    {
        var flow = await PurchasingFlow.StartAsync(factory);
        var requisitionId = await DirectPurchaseRequisitionAsync(flow);

        var tooExpensive = await flow.Buyer.PostAsJsonAsync("/api/v1/purchase-orders", DirectPurchase(requisitionId, flow.Supplier.Id, 6_000m));
        await tooExpensive.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "purchase_order.over_direct_limit");

        var create = await flow.Buyer.PostAsJsonAsync("/api/v1/purchase-orders", DirectPurchase(requisitionId, flow.Supplier.Id, 1_150m));
        var id = (await create.ShouldBeOkAsync<Common.CreatedIdBody>(HttpStatusCode.Created)).Id;

        var po = await flow.GetAsync(id);
        po.Source.Should().Be("DirectPurchase");
        po.RequisitionIds.Should().Equal(requisitionId);
        po.NetTotal.Should().Be(2_400m);
        (await flow.Sourcing.Approvals.GetRequisitionAsync(requisitionId)).Status.Should().Be("Ordered");
    }

    [Fact]
    public async Task A_direct_purchase_needs_a_price_for_every_line()
    {
        var flow = await PurchasingFlow.StartAsync(factory);
        var requisitionId = await DirectPurchaseRequisitionAsync(flow);

        var response = await flow.Buyer.PostAsJsonAsync("/api/v1/purchase-orders", new
        {
            requisitionId,
            supplierId = flow.Supplier.Id,
            lines = new[] { new { requisitionLineNumber = 1, unitPrice = 1_000m, vatCategory = "Standard" } },
        });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "purchase_order.direct_lines_invalid");
    }

    /// <summary>Two monitors and two cables, about 2,100 SAR: under the 5,000 SAR direct purchase limit.</summary>
    private static async Task<Guid> DirectPurchaseRequisitionAsync(PurchasingFlow flow)
    {
        var approvals = flow.Sourcing.Approvals;
        var create = await approvals.Requester.PostAsJsonAsync("/api/v1/requisitions", new
        {
            costCenterId = approvals.CostCenterId,
            title = "Monitors for the support desk",
            justification = "Two support agents need a second screen.",
            neededBy = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
            lines = new[]
            {
                new { description = "27 inch monitor", quantity = 2m, unit = "pcs", estimatedUnitPrice = 1_000m },
                new { description = "HDMI cable", quantity = 2m, unit = "pcs", estimatedUnitPrice = 50m },
            },
        });
        var id = (await create.ShouldBeOkAsync<Common.CreatedIdBody>(HttpStatusCode.Created)).Id;
        (await approvals.Requester.PostAsync($"/api/v1/requisitions/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await ApprovalFlow.ApproveAsync(approvals.Manager, (await approvals.GetRequisitionAsync(id)).ApprovalRequestId!.Value);

        var route = await flow.Buyer.PostAsJsonAsync($"/api/v1/requisitions/{id}/sourcing-route", new { route = "DirectPurchase" });
        route.StatusCode.Should().Be(HttpStatusCode.NoContent, await route.Content.ReadAsStringAsync());
        return id;
    }

    private static object DirectPurchase(Guid requisitionId, Guid supplierId, decimal unitPrice) => new
    {
        requisitionId,
        supplierId,
        lines = new[]
        {
            new { requisitionLineNumber = 1, unitPrice, vatCategory = "Standard" },
            new { requisitionLineNumber = 2, unitPrice = 50m, vatCategory = "Standard" },
        },
    };

    private sealed record PageBody(List<PurchaseOrderSummaryDto> Items);
}
