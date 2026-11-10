using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Mawrid.Application.Files;
using Mawrid.Application.Payables;
using Mawrid.Application.Purchasing;
using Mawrid.Application.Suppliers;
using Mawrid.Domain.Common;

namespace Mawrid.Api.Tests.Infrastructure;

/// <summary>
/// A sent PO (10 laptops at 3,900, 10 docks at 500) with 6 laptops and 10 docks received,
/// plus two AP clerks. The supplier can now invoice.
/// </summary>
public sealed class PayablesFlow
{
    private PayablesFlow(PurchasingFlow purchasing) => Purchasing = purchasing;

    public PurchasingFlow Purchasing { get; }

    public HttpClient ApClerk { get; private set; } = null!;

    public HttpClient SecondApClerk { get; private set; } = null!;

    public HttpClient Admin => Purchasing.Sourcing.Approvals.Admin;

    public HttpClient SupplierUser => Purchasing.Supplier.User;

    public Guid SupplierId => Purchasing.Supplier.Id;

    public string SupplierVatNumber { get; private set; } = null!;

    public Guid PurchaseOrderId { get; private set; }

    public string PurchaseOrderNumber { get; private set; } = null!;

    public static async Task<PayablesFlow> StartAsync(MawridApiFactory factory)
    {
        var flow = new PayablesFlow(await PurchasingFlow.StartAsync(factory));
        var organizationId = flow.Purchasing.Sourcing.Approvals.OrganizationId;

        async Task<HttpClient> Clerk()
        {
            var email = TestData.UniqueEmail("ap");
            await factory.CreateUserAsync(organizationId, email, Role.ApClerk);
            return await factory.CreateClientForAsync(email);
        }

        flow.ApClerk = await Clerk();
        flow.SecondApClerk = await Clerk();

        flow.PurchaseOrderId = await flow.Purchasing.SentPurchaseOrderAsync();
        flow.PurchaseOrderNumber = (await flow.Purchasing.GetAsync(flow.PurchaseOrderId)).Number;
        (await flow.Purchasing.ReceiveAsync(flow.PurchaseOrderId,
                new { lineNumber = 1, acceptedQuantity = 6m }, new { lineNumber = 2, acceptedQuantity = 10m }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var supplier = await (await flow.Purchasing.Buyer.GetAsync($"/api/v1/suppliers/{flow.SupplierId}")).ShouldBeOkAsync<SupplierDto>();
        flow.SupplierVatNumber = supplier.VatNumber;
        return flow;
    }

    /// <summary>An invoice that matches what was received: 6 laptops and 10 docks at the PO prices.</summary>
    public UblInvoiceBuilder MatchingInvoice(string number) =>
        new UblInvoiceBuilder(number, PurchaseOrderNumber, SupplierVatNumber)
            .Line(1, "Laptop 14 inch", 6m, 3_900m)
            .Line(2, "Docking station", 10m, 500m);

    public async Task<HttpResponseMessage> SubmitAsync(UblInvoiceBuilder invoice, HttpClient? supplier = null) =>
        await SubmitXmlAsync(invoice.Build(), supplier);

    public async Task<HttpResponseMessage> SubmitXmlAsync(byte[] xml, HttpClient? supplier = null)
    {
        var client = supplier ?? SupplierUser;
        var file = await UploadAsync(client, xml, "invoice.xml", "application/xml");
        return await client.PostAsJsonAsync("/api/v1/supplier/invoices", new { xmlFileId = file.Id, pdfFileId = (Guid?)null });
    }

    public async Task<Guid> SubmittedInvoiceAsync(UblInvoiceBuilder invoice) =>
        (await (await SubmitAsync(invoice)).ShouldBeOkAsync<Common.CreatedIdBody>(HttpStatusCode.Created)).Id;

    public async Task<InvoiceDto> GetAsync(Guid id) =>
        await (await ApClerk.GetAsync($"/api/v1/invoices/{id}")).ShouldBeOkAsync<InvoiceDto>();

    public async Task<PurchaseOrderDto> PurchaseOrderAsync() => await Purchasing.GetAsync(PurchaseOrderId);

    public static async Task<FileDto> UploadAsync(HttpClient client, byte[] content, string fileName, string contentType)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return await (await client.PostAsync("/api/v1/files", form)).ShouldBeOkAsync<FileDto>(HttpStatusCode.Created);
    }
}
