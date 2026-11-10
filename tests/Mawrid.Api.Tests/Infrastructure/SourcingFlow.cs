using System.Net;
using System.Net.Http.Json;
using Mawrid.Application.Sourcing;

namespace Mawrid.Api.Tests.Infrastructure;

/// <summary>An organization with an approved requisition (route RFQ) and three approved suppliers.</summary>
public sealed class SourcingFlow
{
    private SourcingFlow(ApprovalFlow approvals, SupplierFlow suppliers)
    {
        Approvals = approvals;
        Suppliers = suppliers;
    }

    public ApprovalFlow Approvals { get; }

    public SupplierFlow Suppliers { get; }

    public HttpClient Buyer => Approvals.Buyer;

    public Guid RequisitionId { get; private set; }

    public List<(Guid Id, HttpClient User)> ApprovedSuppliers { get; } = [];

    public static async Task<SourcingFlow> StartAsync(MawridApiFactory factory, int supplierCount = 3)
    {
        var approvals = await ApprovalFlow.StartAsync(factory);
        var flow = new SourcingFlow(approvals, SupplierFlow.For(factory, approvals.OrganizationId, approvals.Admin, approvals.Buyer));

        for (var i = 0; i < supplierCount; i++)
        {
            flow.ApprovedSuppliers.Add(await flow.Suppliers.ApprovedSupplierAsync());
        }

        flow.RequisitionId = await flow.ApprovedRequisitionForRfqAsync();
        return flow;
    }

    /// <summary>A two-line requisition (10 laptops, 10 docks), approved and routed to RFQ.</summary>
    public async Task<Guid> ApprovedRequisitionForRfqAsync()
    {
        var create = await Approvals.Requester.PostAsJsonAsync("/api/v1/requisitions", new
        {
            costCenterId = Approvals.CostCenterId,
            title = "Laptops and docks",
            justification = "Ten new engineers join next month and need equipment.",
            neededBy = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(40)),
            lines = new[]
            {
                new { description = "Laptop 14 inch", quantity = 10m, unit = "pcs", estimatedUnitPrice = 4_000m },
                new { description = "Docking station", quantity = 10m, unit = "pcs", estimatedUnitPrice = 500m },
            },
        });
        var id = (await create.ShouldBeOkAsync<Common.CreatedIdBody>(HttpStatusCode.Created)).Id;
        (await Approvals.Requester.PostAsync($"/api/v1/requisitions/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 45,000 net + VAT = 51,750: manager then director.
        var pr = await Approvals.GetRequisitionAsync(id);
        await ApprovalFlow.ApproveAsync(Approvals.Manager, pr.ApprovalRequestId!.Value);
        await ApprovalFlow.ApproveAsync(Approvals.Director, pr.ApprovalRequestId!.Value);

        var route = await Buyer.PostAsJsonAsync($"/api/v1/requisitions/{id}/sourcing-route", new { route = "Rfq" });
        route.StatusCode.Should().Be(HttpStatusCode.NoContent, await route.Content.ReadAsStringAsync());
        return id;
    }

    public async Task<Guid> CreateRfqAsync(params Guid[] requisitionIds)
    {
        var response = await Buyer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            title = "Laptops and docks Q4",
            description = "Business laptops with 3-year warranty.",
            requisitionIds = requisitionIds.Length > 0 ? requisitionIds : [RequisitionId],
        });
        return (await response.ShouldBeOkAsync<Common.CreatedIdBody>(HttpStatusCode.Created)).Id;
    }

    /// <summary>Creates, invites all suppliers, sets the deadline 4 days ahead and publishes.</summary>
    public async Task<Guid> PublishedRfqAsync()
    {
        var rfqId = await CreateRfqAsync();
        (await Buyer.PostAsJsonAsync($"/api/v1/rfqs/{rfqId}/invitations", new { supplierIds = ApprovedSuppliers.Select(s => s.Id) }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Buyer.PutAsJsonAsync($"/api/v1/rfqs/{rfqId}/deadline", new { deadline = DateTimeOffset.UtcNow.AddDays(4) }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var publish = await Buyer.PostAsJsonAsync($"/api/v1/rfqs/{rfqId}/publish", new { singleSourceReason = (string?)null });
        publish.StatusCode.Should().Be(HttpStatusCode.NoContent, await publish.Content.ReadAsStringAsync());
        return rfqId;
    }

    public static async Task<HttpResponseMessage> QuoteAsync(HttpClient supplier, Guid rfqId, decimal laptopPrice, decimal? dockPrice, IReadOnlyList<Guid>? attachments = null)
    {
        var lines = new List<object> { new { itemLineNumber = 1, unitPrice = laptopPrice, vatCategory = "Standard", leadTimeDays = 10 } };
        if (dockPrice is not null)
        {
            lines.Add(new { itemLineNumber = 2, unitPrice = dockPrice.Value, vatCategory = "Standard", leadTimeDays = 5 });
        }

        return await supplier.PutAsJsonAsync($"/api/v1/supplier/rfqs/{rfqId}/quotation", new
        {
            validUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60)),
            lines,
            notes = "Prices include delivery to Riyadh.",
            attachmentFileIds = attachments ?? [],
        });
    }

    public async Task<ComparisonDto> ComparisonAsync(Guid rfqId) =>
        await (await Buyer.GetAsync($"/api/v1/rfqs/{rfqId}/comparison")).ShouldBeOkAsync<ComparisonDto>();
}
