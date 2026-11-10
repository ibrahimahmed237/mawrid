using System.Net;
using System.Net.Http.Json;
using Mawrid.Api.Tests.Infrastructure;
using Mawrid.Application.Payables.Extraction;

namespace Mawrid.Api.Tests.Payables;

[Collection(ApiCollection.Name)]
public sealed class ExtractionTests(MawridApiFactory factory)
{
    [Fact]
    public async Task A_pdf_invoice_becomes_a_draft_that_the_clerk_confirms()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        var pdf = await UploadInvoicePdfAsync(flow);
        factory.Llm.Status = HttpStatusCode.OK;
        factory.Llm.Answer = ModelAnswer(flow, "PDF-501");

        var draft = await (await flow.ApClerk.PostAsJsonAsync("/api/v1/invoices/extract", new { pdfFileId = pdf, purchaseOrderId = flow.PurchaseOrderId }))
            .ShouldBeOkAsync<InvoiceDraftDto>();

        draft.Warnings.Should().BeEmpty();
        draft.SupplierInvoiceNumber.Value.Should().Be("PDF-501");
        draft.Lines.Select(l => l.PoLineNumber).Should().Equal(1, 2);
        draft.Model.Should().Be("fake-model");

        // The prompt carried the PDF text and the PO lines, and asked for JSON.
        var request = factory.Llm.LastRequest!.RootElement;
        request.GetProperty("response_format").GetProperty("type").GetString().Should().Be("json_object");
        var user = request.GetProperty("messages")[1].GetProperty("content").GetString();
        user.Should().Contain("Invoice No: PDF-501").And.Contain("- line 1: Laptop 14 inch");

        // The clerk confirms the draft; a second clerk releases it.
        var create = await flow.ApClerk.PostAsJsonAsync("/api/v1/invoices", new
        {
            purchaseOrderId = flow.PurchaseOrderId,
            supplierInvoiceNumber = draft.SupplierInvoiceNumber.Value,
            issueDate = draft.IssueDate.Value,
            lines = draft.Lines.Select(l => new { poLineNumber = l.PoLineNumber, l.Description, l.Quantity, l.UnitPrice, l.VatCategory, l.NetAmount, l.VatAmount }),
            netTotal = draft.NetTotal.Value,
            vatTotal = draft.VatTotal.Value,
            total = draft.Total.Value,
            qrCode = (string?)null,
            pdfFileId = pdf,
            extractedWithAi = true,
        });
        var id = (await create.ShouldBeOkAsync<Common.CreatedIdBody>(HttpStatusCode.Created)).Id;
        var invoice = await flow.GetAsync(id);
        invoice.Source.Should().Be("AiAssisted");
        invoice.Holds.Select(h => h.Code).Should().Equal("qr.missing");

        (await flow.ApClerk.PostAsJsonAsync($"/api/v1/invoices/{id}/overrides", new { code = "qr.missing", reason = "QR on the paper copy checked by hand" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        await (await flow.ApClerk.PostAsync($"/api/v1/invoices/{id}/release", null)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "invoice.same_person");
        (await flow.SecondApClerk.PostAsync($"/api/v1/invoices/{id}/release", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_scanned_pdf_without_text_is_refused_before_calling_the_ai()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        var scan = await PayablesFlow.UploadAsync(flow.ApClerk, TestPdf.WithLines(), "scan.pdf", "application/pdf");
        var calls = factory.Llm.Calls;

        var response = await flow.ApClerk.PostAsJsonAsync("/api/v1/invoices/extract", new { pdfFileId = scan.Id, purchaseOrderId = (Guid?)null });

        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "extraction.no_text");
        factory.Llm.Calls.Should().Be(calls);
    }

    [Fact]
    public async Task An_unavailable_ai_provider_gives_503()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        var pdf = await UploadInvoicePdfAsync(flow);
        factory.Llm.Status = HttpStatusCode.ServiceUnavailable;
        try
        {
            var response = await flow.ApClerk.PostAsJsonAsync("/api/v1/invoices/extract", new { pdfFileId = pdf, purchaseOrderId = flow.PurchaseOrderId });

            await response.ShouldBeProblemAsync(HttpStatusCode.ServiceUnavailable, "extraction.provider_unavailable");
        }
        finally
        {
            factory.Llm.Status = HttpStatusCode.OK;
        }
    }

    [Fact]
    public async Task Only_pdf_files_can_be_extracted_and_only_by_ap()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        var xml = await PayablesFlow.UploadAsync(flow.ApClerk, flow.MatchingInvoice("X-1").Build(), "invoice.xml", "application/xml");

        await (await flow.ApClerk.PostAsJsonAsync("/api/v1/invoices/extract", new { pdfFileId = xml.Id, purchaseOrderId = (Guid?)null }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "extraction.not_pdf");
        (await flow.Purchasing.Buyer.PostAsJsonAsync("/api/v1/invoices/extract", new { pdfFileId = xml.Id, purchaseOrderId = (Guid?)null }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<Guid> UploadInvoicePdfAsync(PayablesFlow flow)
    {
        var bytes = TestPdf.WithLines(
            "Al Ufuq Supplies - Tax Invoice",
            $"VAT No: {flow.SupplierVatNumber}",
            "Invoice No: PDF-501    Date: 2026-11-09",
            $"Your PO: {flow.PurchaseOrderNumber}",
            "1  Laptop 14 inch    6 x 3,900.00 = 23,400.00   VAT 15% 3,510.00",
            "2  Docking station  10 x   500.00 =  5,000.00   VAT 15%   750.00",
            "Total before VAT 28,400.00   VAT 4,260.00   Total 32,660.00");
        return (await PayablesFlow.UploadAsync(flow.ApClerk, bytes, "invoice.pdf", "application/pdf")).Id;
    }

    /// <summary>What a good model answers for the PDF above.</summary>
    private static string ModelAnswer(PayablesFlow flow, string number) => $$"""
        {
          "supplierInvoiceNumber": {"value": "{{number}}", "confidence": 0.97},
          "issueDate": {"value": "2026-11-09", "confidence": 0.95},
          "sellerVatNumber": {"value": "{{flow.SupplierVatNumber}}", "confidence": 0.96},
          "purchaseOrderNumber": {"value": "{{flow.PurchaseOrderNumber}}", "confidence": 0.93},
          "netTotal": {"value": 28400.00, "confidence": 0.95},
          "vatTotal": {"value": 4260.00, "confidence": 0.95},
          "total": {"value": 32660.00, "confidence": 0.95},
          "lines": [
            {"poLineNumber": 1, "description": "Laptop 14 inch", "quantity": 6, "unitPrice": 3900, "vatRatePercent": 15, "netAmount": 23400, "vatAmount": 3510, "confidence": 0.92},
            {"poLineNumber": 2, "description": "Docking station", "quantity": 10, "unitPrice": 500, "vatRatePercent": 15, "netAmount": 5000, "vatAmount": 750, "confidence": 0.92}
          ]
        }
        """;
}
