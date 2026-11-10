using System.Net;
using System.Net.Http.Json;
using System.Text;
using Mawrid.Api.Tests.Infrastructure;
using Mawrid.Application.Payables;
using Mawrid.Application.Suppliers;

namespace Mawrid.Api.Tests.Payables;

[Collection(ApiCollection.Name)]
public sealed class InvoiceTests(MawridApiFactory factory)
{
    [Fact]
    public async Task A_matching_e_invoice_is_matched_released_and_recorded_on_the_po()
    {
        var flow = await PayablesFlow.StartAsync(factory);

        var id = await flow.SubmittedInvoiceAsync(flow.MatchingInvoice("INV-1001"));

        var invoice = await flow.GetAsync(id);
        invoice.Status.Should().Be("Matched");
        invoice.Source.Should().Be("UblXml");
        invoice.Total.Should().Be(32_660m); // (6 x 3,900 + 10 x 500) x 1.15
        invoice.Holds.Should().BeEmpty();

        (await flow.ApClerk.PostAsync($"/api/v1/invoices/{id}/release", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await flow.GetAsync(id)).Status.Should().Be("ReleasedForPayment");
        var po = await flow.PurchaseOrderAsync();
        po.Lines.Select(l => l.InvoicedQuantity).Should().Equal(6m, 10m);
        var mine = await (await flow.SupplierUser.GetAsync($"/api/v1/supplier/invoices/{id}")).ShouldBeOkAsync<SupplierInvoiceDto>();
        mine.Status.Should().Be("ReleasedForPayment");
    }

    [Fact]
    public async Task A_price_above_the_tolerance_is_held_until_an_admin_overrides_it()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        var invoice = new UblInvoiceBuilder("INV-2001", flow.PurchaseOrderNumber, flow.SupplierVatNumber).Line(1, "Laptop 14 inch", 6m, 4_100m);

        var id = await flow.SubmittedInvoiceAsync(invoice);

        var held = await flow.GetAsync(id);
        held.Status.Should().Be("OnHold");
        held.Holds.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Code = "match.price", LineNumber = 1, Expected = "3900.00", Actual = "4100.00" });

        var byClerk = await flow.ApClerk.PostAsJsonAsync($"/api/v1/invoices/{id}/overrides",
            new { code = "match.price", lineNumber = 1, reason = "Price increase agreed by phone" });
        await byClerk.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "invoice.override_needs_admin");

        (await flow.Admin.PostAsJsonAsync($"/api/v1/invoices/{id}/overrides",
            new { code = "match.price", lineNumber = 1, reason = "Price increase agreed by phone" })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var matched = await flow.GetAsync(id);
        matched.Status.Should().Be("Matched");
        matched.Holds.Single().OverrideReason.Should().Be("Price increase agreed by phone");
    }

    [Fact]
    public async Task A_quantity_hold_clears_by_itself_when_the_rest_of_the_goods_arrive()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        var invoice = new UblInvoiceBuilder("INV-3001", flow.PurchaseOrderNumber, flow.SupplierVatNumber).Line(1, "Laptop 14 inch", 10m, 3_900m);
        var id = await flow.SubmittedInvoiceAsync(invoice);
        (await flow.GetAsync(id)).Holds.Should().ContainSingle(h => h.Code == "match.quantity" && h.Expected == "6" && h.Actual == "10");

        var forced = await flow.ApClerk.PostAsJsonAsync($"/api/v1/invoices/{id}/overrides",
            new { code = "match.quantity", lineNumber = 1, reason = "The rest arrives tomorrow" });
        await forced.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "invoice.hold_not_overridable");

        (await flow.Purchasing.ReceiveAsync(flow.PurchaseOrderId, new { lineNumber = 1, acceptedQuantity = 4m })).StatusCode.Should().Be(HttpStatusCode.Created);
        await factory.DeliverOutboxAsync();

        (await flow.GetAsync(id)).Status.Should().Be("Matched");
    }

    [Fact]
    public async Task A_pending_bank_change_holds_the_invoice_until_the_account_is_verified()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        (await flow.SupplierUser.PostAsJsonAsync("/api/v1/supplier/bank-accounts",
            new { iban = SupplierFlow.OtherIban, bankName = "Saudi National Bank", accountName = "Al Ufuq Supplies" })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var id = await flow.SubmittedInvoiceAsync(flow.MatchingInvoice("INV-4001"));
        (await flow.GetAsync(id)).Holds.Should().ContainSingle().Which.Code.Should().Be("supplier.bank_unverified");

        var supplier = await (await flow.Admin.GetAsync($"/api/v1/suppliers/{flow.SupplierId}")).ShouldBeOkAsync<SupplierDto>();
        var pending = supplier.BankAccounts.Single(a => a.Status == "PendingVerification");
        (await flow.Admin.PostAsync($"/api/v1/suppliers/{flow.SupplierId}/bank-accounts/{pending.Id}/verify", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        await factory.DeliverOutboxAsync();

        (await flow.GetAsync(id)).Status.Should().Be("Matched");
    }

    [Fact]
    public async Task A_wrong_or_missing_qr_code_is_a_hold()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        var noQr = flow.MatchingInvoice("INV-5001");
        noQr.QrOverride = null;
        var foreignQr = flow.MatchingInvoice("INV-5002");
        foreignQr.QrOverride = new Mawrid.Domain.Payables.ZatcaQr("Someone", "399999999900003", "2026-01-01T00:00:00Z", 32_660m, 4_260m).Encode();

        (await flow.GetAsync(await flow.SubmittedInvoiceAsync(noQr))).Holds.Select(h => h.Code).Should().Equal("qr.missing");
        (await flow.GetAsync(await flow.SubmittedInvoiceAsync(foreignQr))).Holds.Select(h => h.Code).Should().Contain("qr.vat_mismatch");
    }

    [Fact]
    public async Task The_same_invoice_number_twice_is_a_duplicate()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        await flow.SubmittedInvoiceAsync(flow.MatchingInvoice("INV-6001"));

        var again = await flow.SubmitAsync(flow.MatchingInvoice("INV-6001"));

        await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "invoice.duplicate");
    }

    [Fact]
    public async Task A_supplier_cannot_invoice_another_suppliers_po()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        var (otherId, other) = flow.Purchasing.Sourcing.ApprovedSuppliers[1];
        var otherVat = (await (await flow.Admin.GetAsync($"/api/v1/suppliers/{otherId}")).ShouldBeOkAsync<SupplierDto>()).VatNumber;
        var invoice = new UblInvoiceBuilder("INV-7001", flow.PurchaseOrderNumber, otherVat).Line(1, "Laptop 14 inch", 6m, 3_900m);

        var response = await flow.SubmitAsync(invoice, other);

        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "invoice.po_not_invoiceable");
    }

    [Fact]
    public async Task An_invoice_with_another_sellers_vat_number_is_refused()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        var invoice = new UblInvoiceBuilder("INV-7101", flow.PurchaseOrderNumber, "399999999900003").Line(1, "Laptop 14 inch", 6m, 3_900m);

        await (await flow.SubmitAsync(invoice)).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "invoice.seller_vat_mismatch");
    }

    [Fact]
    public async Task A_file_that_is_not_a_ubl_invoice_is_refused_and_doctype_attacks_are_blocked()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        const string Xxe = """
            <?xml version="1.0"?>
            <!DOCTYPE Invoice [ <!ENTITY secret SYSTEM "file:///etc/passwd"> ]>
            <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"><ID>&secret;</ID></Invoice>
            """;

        await (await flow.SubmitXmlAsync(Encoding.UTF8.GetBytes("<order><id>1</id></order>")))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invoice.source_file_invalid");
        await (await flow.SubmitXmlAsync(Encoding.UTF8.GetBytes(Xxe)))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invoice.source_file_invalid");
    }

    [Fact]
    public async Task A_typed_in_invoice_needs_a_second_clerk_to_release_it()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        var pdf = await SupplierFlow.UploadPdfAsync(flow.ApClerk);
        var qr = new Mawrid.Domain.Payables.ZatcaQr("Al Ufuq Supplies", flow.SupplierVatNumber, "2026-11-01T10:00:00Z", 2_875m, 375m).Encode();

        var create = await flow.ApClerk.PostAsJsonAsync("/api/v1/invoices", new
        {
            purchaseOrderId = flow.PurchaseOrderId,
            supplierInvoiceNumber = "PAPER-77",
            issueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            lines = new[] { new { poLineNumber = 2, description = "Docking station", quantity = 5m, unitPrice = 500m, vatCategory = "Standard", netAmount = 2_500m, vatAmount = 375m } },
            netTotal = 2_500m,
            vatTotal = 375m,
            total = 2_875m,
            qrCode = qr,
            pdfFileId = pdf.Id,
        });
        var id = (await create.ShouldBeOkAsync<Common.CreatedIdBody>(HttpStatusCode.Created)).Id;
        (await flow.GetAsync(id)).Status.Should().Be("Matched");

        await (await flow.ApClerk.PostAsync($"/api/v1/invoices/{id}/release", null)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "invoice.same_person");
        (await flow.SecondApClerk.PostAsync($"/api/v1/invoices/{id}/release", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_rejected_invoice_frees_its_quantities_and_the_supplier_sees_the_reason()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        var first = await flow.SubmittedInvoiceAsync(flow.MatchingInvoice("INV-8001"));
        (await flow.ApClerk.PostAsJsonAsync($"/api/v1/invoices/{first}/reject", new { reason = "Wrong bank details on the PDF" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await flow.SubmittedInvoiceAsync(flow.MatchingInvoice("INV-8002"));

        (await flow.GetAsync(second)).Status.Should().Be("Matched", "the rejected invoice no longer counts as invoiced");
        var mine = await (await flow.SupplierUser.GetAsync($"/api/v1/supplier/invoices/{first}")).ShouldBeOkAsync<SupplierInvoiceDto>();
        mine.Status.Should().Be("Rejected");
        mine.RejectionReason.Should().Be("Wrong bank details on the PDF");
    }

    [Fact]
    public async Task Suppliers_see_in_review_without_hold_details_and_cannot_use_the_ap_api()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        var invoice = new UblInvoiceBuilder("INV-9001", flow.PurchaseOrderNumber, flow.SupplierVatNumber).Line(1, "Laptop 14 inch", 6m, 4_100m);
        var id = await flow.SubmittedInvoiceAsync(invoice);

        var json = await (await flow.SupplierUser.GetAsync($"/api/v1/supplier/invoices/{id}")).Content.ReadAsStringAsync();
        json.Should().Contain("\"status\":\"InReview\"").And.NotContain("match.price");
        (await flow.SupplierUser.GetAsync($"/api/v1/invoices/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await flow.Purchasing.Buyer.GetAsync($"/api/v1/invoices/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_ap_work_list_filters_by_status()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        var held = await flow.SubmittedInvoiceAsync(new UblInvoiceBuilder("INV-10001", flow.PurchaseOrderNumber, flow.SupplierVatNumber).Line(1, "Laptop", 6m, 4_100m));

        var list = await (await flow.ApClerk.GetAsync($"/api/v1/invoices?status=OnHold&purchaseOrderId={flow.PurchaseOrderId}")).ShouldBeOkAsync<PageBody>();

        list.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = held, OpenHolds = 1, Status = "OnHold" });
    }

    private sealed record PageBody(List<InvoiceSummaryDto> Items);
}
