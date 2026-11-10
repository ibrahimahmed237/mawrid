using System.Net;
using System.Net.Http.Json;
using Mawrid.Api.Tests.Infrastructure;
using Mawrid.Application.Purchasing;
using Mawrid.Application.Sourcing;

namespace Mawrid.Api.Tests.Sourcing;

[Collection(ApiCollection.Name)]
public class RfqTests(MawridApiFactory factory)
{
    [Fact]
    public async Task From_requisition_to_sealed_quotes_to_award_and_purchase_orders()
    {
        var flow = await SourcingFlow.StartAsync(factory);
        var (supplierA, userA) = flow.ApprovedSuppliers[0];
        var (supplierB, userB) = flow.ApprovedSuppliers[1];
        var (_, userC) = flow.ApprovedSuppliers[2];
        var rfqId = await flow.PublishedRfqAsync();

        (await SourcingFlow.QuoteAsync(userA, rfqId, 3_900m, 520m)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SourcingFlow.QuoteAsync(userB, rfqId, 4_100m, 480m)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SourcingFlow.QuoteAsync(userC, rfqId, 4_050m, null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var submissions = await (await flow.Buyer.GetAsync($"/api/v1/rfqs/{rfqId}/submissions")).ShouldBeOkAsync<List<SubmissionDto>>();
        var sealedComparison = await flow.Buyer.GetAsync($"/api/v1/rfqs/{rfqId}/comparison");
        submissions.Should().HaveCount(3);
        (await flow.Buyer.GetStringAsync($"/api/v1/rfqs/{rfqId}/submissions")).Should().NotContain("3900");
        await sealedComparison.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "rfq.still_open");

        using (factory.Clock.Travel(TimeSpan.FromDays(5)))
        {
            var late = await SourcingFlow.QuoteAsync(userA, rfqId, 3_000m, 400m);
            var comparison = await flow.ComparisonAsync(rfqId);
            var award = await flow.Buyer.PostAsJsonAsync($"/api/v1/rfqs/{rfqId}/award", new
            {
                lines = new object[]
                {
                    new { itemLineNumber = 1, supplierId = supplierA },
                    new { itemLineNumber = 2, supplierId = supplierA, justification = "One supplier for both items keeps delivery simple." },
                },
            });

            await late.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "rfq.deadline_passed");
            comparison.Items[0].Quotes.Should().HaveCount(3);
            comparison.Items[0].Quotes.Single(q => q.IsLowest).SupplierId.Should().Be(supplierA);
            comparison.Items[1].Quotes.Should().HaveCount(2, "supplier C did not bid on docks");
            comparison.Items[1].Quotes.Single(q => q.IsLowest).SupplierId.Should().Be(supplierB);

            var orders = await award.ShouldBeOkAsync<AwardBody>();
            orders.PurchaseOrderIds.Should().ContainSingle();
            var po = await (await flow.Buyer.GetAsync($"/api/v1/purchase-orders/{orders.PurchaseOrderIds[0]}")).ShouldBeOkAsync<PurchaseOrderDto>();
            po.Status.Should().Be("Draft");
            po.SupplierId.Should().Be(supplierA);
            po.Number.Should().MatchRegex(@"^PO-\d{4}-\d{6}$");
            po.NetTotal.Should().Be(10 * 3_900m + 10 * 520m);
            po.VatTotal.Should().Be((10 * 3_900m + 10 * 520m) * 0.15m);
            po.RequisitionIds.Should().Equal(flow.RequisitionId);
        }

        var rfq = await (await flow.Buyer.GetAsync($"/api/v1/rfqs/{rfqId}")).ShouldBeOkAsync<RfqDto>();
        rfq.Status.Should().Be("Awarded");
        rfq.Awards.Single(a => a.ItemLineNumber == 2).IsLowestPrice.Should().BeFalse();
        (await flow.Approvals.GetRequisitionAsync(flow.RequisitionId)).Status.Should().Be("Ordered");
        var forA = await (await userA.GetAsync($"/api/v1/supplier/rfqs/{rfqId}")).ShouldBeOkAsync<SupplierRfqDto>();
        forA.ItemsAwardedToMe.Should().Equal(1, 2);
    }

    private sealed record AwardBody(List<Guid> PurchaseOrderIds);

    [Fact]
    public async Task Choosing_a_higher_price_needs_a_justification()
    {
        var flow = await SourcingFlow.StartAsync(factory);
        var rfqId = await flow.PublishedRfqAsync();
        await SourcingFlow.QuoteAsync(flow.ApprovedSuppliers[0].User, rfqId, 3_900m, 500m);
        await SourcingFlow.QuoteAsync(flow.ApprovedSuppliers[1].User, rfqId, 4_100m, 500m);

        using var travel = factory.Clock.Travel(TimeSpan.FromDays(5));
        var award = await flow.Buyer.PostAsJsonAsync($"/api/v1/rfqs/{rfqId}/award", new
        {
            lines = new[] { new { itemLineNumber = 1, supplierId = flow.ApprovedSuppliers[1].Id } },
        });

        await award.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "rfq.justification_required");
    }

    [Fact]
    public async Task Fewer_suppliers_than_the_minimum_need_a_single_source_reason()
    {
        var flow = await SourcingFlow.StartAsync(factory, supplierCount: 1);
        var rfqId = await flow.CreateRfqAsync();
        await flow.Buyer.PutAsJsonAsync($"/api/v1/rfqs/{rfqId}/deadline", new { deadline = DateTimeOffset.UtcNow.AddDays(4) });

        var invite = await flow.Buyer.PostAsJsonAsync($"/api/v1/rfqs/{rfqId}/invitations", new { supplierIds = new[] { flow.ApprovedSuppliers[0].Id } });
        invite.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var withoutReason = await flow.Buyer.PostAsJsonAsync($"/api/v1/rfqs/{rfqId}/publish", new { singleSourceReason = (string?)null });
        var withReason = await flow.Buyer.PostAsJsonAsync($"/api/v1/rfqs/{rfqId}/publish", new { singleSourceReason = "Sole authorized distributor in KSA" });

        await withoutReason.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "rfq.single_source_reason_required");
        withReason.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Only_awardable_suppliers_can_be_invited()
    {
        var flow = await SourcingFlow.StartAsync(factory, supplierCount: 1);
        var notApproved = await flow.Suppliers.RegisterAsync();
        var rfqId = await flow.CreateRfqAsync();

        var invite = await flow.Buyer.PostAsJsonAsync($"/api/v1/rfqs/{rfqId}/invitations", new { supplierIds = new[] { notApproved } });

        await invite.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "rfq.supplier_not_awardable");
    }

    [Fact]
    public async Task After_an_amendment_an_unconfirmed_quote_cannot_be_awarded()
    {
        var flow = await SourcingFlow.StartAsync(factory);
        var rfqId = await flow.PublishedRfqAsync();
        var (supplierA, userA) = flow.ApprovedSuppliers[0];
        await SourcingFlow.QuoteAsync(userA, rfqId, 3_900m, 500m);

        var amend = await flow.Buyer.PostAsJsonAsync($"/api/v1/rfqs/{rfqId}/amend",
            new { deadline = DateTimeOffset.UtcNow.AddDays(6), quantities = new Dictionary<int, decimal> { [1] = 12 } });
        var view = await (await userA.GetAsync($"/api/v1/supplier/rfqs/{rfqId}")).ShouldBeOkAsync<SupplierRfqDto>();

        amend.StatusCode.Should().Be(HttpStatusCode.NoContent, await amend.Content.ReadAsStringAsync());
        view.Revision.Should().Be(1);
        view.Items[0].Quantity.Should().Be(12);
        view.MyQuotation!.NeedsReconfirmation.Should().BeTrue();

        using var travel = factory.Clock.Travel(TimeSpan.FromDays(7));
        var award = await flow.Buyer.PostAsJsonAsync($"/api/v1/rfqs/{rfqId}/award", new
        {
            lines = new[] { new { itemLineNumber = 1, supplierId = supplierA } },
        });
        await award.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "rfq.award_invalid");
    }

    [Fact]
    public async Task Suppliers_see_only_published_rfqs_they_are_invited_to()
    {
        var flow = await SourcingFlow.StartAsync(factory);
        var draftId = await flow.CreateRfqAsync(await flow.ApprovedRequisitionForRfqAsync());
        await flow.Buyer.PostAsJsonAsync($"/api/v1/rfqs/{draftId}/invitations", new { supplierIds = new[] { flow.ApprovedSuppliers[0].Id } });
        var outsider = await flow.Suppliers.ApprovedSupplierAsync();
        var publishedId = await flow.PublishedRfqAsync();

        var draftView = await flow.ApprovedSuppliers[0].User.GetAsync($"/api/v1/supplier/rfqs/{draftId}");
        var outsiderView = await outsider.SupplierUser.GetAsync($"/api/v1/supplier/rfqs/{publishedId}");
        var outsiderQuote = await SourcingFlow.QuoteAsync(outsider.SupplierUser, publishedId, 1m, null);
        var list = await (await flow.ApprovedSuppliers[0].User.GetAsync("/api/v1/supplier/rfqs")).ShouldBeOkAsync<List<SupplierRfqDto>>();

        await draftView.ShouldBeProblemAsync(HttpStatusCode.NotFound, "rfq.not_found");
        await outsiderView.ShouldBeProblemAsync(HttpStatusCode.NotFound, "rfq.not_found");
        await outsiderQuote.ShouldBeProblemAsync(HttpStatusCode.NotFound, "rfq.not_found");
        list.Should().ContainSingle(r => r.Id == publishedId);
    }

    [Fact]
    public async Task A_requisition_must_be_routed_to_rfq_before_it_can_be_sourced()
    {
        var flow = await SourcingFlow.StartAsync(factory, supplierCount: 0);
        var draftPr = await flow.Approvals.CreateRequisitionAsync(1_000m);

        var response = await flow.Buyer.PostAsJsonAsync("/api/v1/rfqs", new { title = "Not ready", requisitionIds = new[] { draftPr } });

        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "requisition.not_ready_for_sourcing");
    }

    [Fact]
    public async Task Quotation_attachments_must_belong_to_the_supplier()
    {
        var flow = await SourcingFlow.StartAsync(factory);
        var rfqId = await flow.PublishedRfqAsync();
        var userA = flow.ApprovedSuppliers[0].User;
        var userB = flow.ApprovedSuppliers[1].User;
        var fileOfA = await SupplierFlow.UploadPdfAsync(userA, "%PDF-1.7 datasheet");

        var own = await SourcingFlow.QuoteAsync(userA, rfqId, 3_900m, 500m, [fileOfA.Id]);
        var foreign = await SourcingFlow.QuoteAsync(userB, rfqId, 3_900m, 500m, [fileOfA.Id]);

        own.StatusCode.Should().Be(HttpStatusCode.OK);
        await foreign.ShouldBeProblemAsync(HttpStatusCode.NotFound, "quotation.attachment_not_found");
    }

    [Fact]
    public async Task Draft_purchase_orders_are_hidden_from_suppliers()
    {
        var flow = await SourcingFlow.StartAsync(factory);
        var rfqId = await flow.PublishedRfqAsync();
        var (supplierA, userA) = flow.ApprovedSuppliers[0];
        await SourcingFlow.QuoteAsync(userA, rfqId, 3_900m, 500m);
        Guid poId;
        using (factory.Clock.Travel(TimeSpan.FromDays(5)))
        {
            var award = await flow.Buyer.PostAsJsonAsync($"/api/v1/rfqs/{rfqId}/award",
                new { lines = new[] { new { itemLineNumber = 1, supplierId = supplierA } } });
            poId = (await award.ShouldBeOkAsync<AwardBody>()).PurchaseOrderIds.Single();
        }

        var supplierView = await userA.GetAsync($"/api/v1/purchase-orders/{poId}");

        await supplierView.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "auth.forbidden");
    }

    [Fact]
    public async Task A_supplier_can_revise_its_quotation_before_the_deadline()
    {
        var flow = await SourcingFlow.StartAsync(factory);
        var rfqId = await flow.PublishedRfqAsync();
        var user = flow.ApprovedSuppliers[0].User;

        await SourcingFlow.QuoteAsync(user, rfqId, 4_000m, 500m);
        (await SourcingFlow.QuoteAsync(user, rfqId, 3_850m, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var view = await (await user.GetAsync($"/api/v1/supplier/rfqs/{rfqId}")).ShouldBeOkAsync<SupplierRfqDto>();

        view.MyQuotation!.Version.Should().Be(2);
        view.MyQuotation.Lines.Should().ContainSingle().Which.UnitPrice.Should().Be(3_850m);
    }
}
