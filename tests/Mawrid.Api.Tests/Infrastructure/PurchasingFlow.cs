using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Mawrid.Application.Purchasing;
using Mawrid.Domain.Common;

namespace Mawrid.Api.Tests.Infrastructure;

/// <summary>
/// A sourcing flow plus what purchasing needs: a PO approval policy (manager up to 100,000 SAR,
/// then the director too) and a receiver.
/// </summary>
public sealed class PurchasingFlow
{
    private PurchasingFlow(SourcingFlow sourcing) => Sourcing = sourcing;

    public SourcingFlow Sourcing { get; }

    public MawridApiFactory Factory => Sourcing.Approvals.Factory;

    public HttpClient Buyer => Sourcing.Buyer;

    public HttpClient Manager => Sourcing.Approvals.Manager;

    public HttpClient Receiver { get; private set; } = null!;

    public (Guid Id, HttpClient User) Supplier => Sourcing.ApprovedSuppliers[0];

    public static async Task<PurchasingFlow> StartAsync(MawridApiFactory factory)
    {
        var flow = new PurchasingFlow(await SourcingFlow.StartAsync(factory));
        var approvals = flow.Sourcing.Approvals;

        var email = TestData.UniqueEmail("receiver");
        await factory.CreateUserAsync(approvals.OrganizationId, email, Role.Receiver);
        flow.Receiver = await factory.CreateClientForAsync(email);

        var directors = await approvals.CreateGroupAsync("PO directors", approvals.DirectorId);
        var response = await approvals.Admin.PutAsJsonAsync("/api/v1/approval-policies/PurchaseOrder", new
        {
            rules = new object[]
            {
                new { upToAmount = (decimal?)100_000m, steps = new object[] { new { name = "Cost center manager", kind = "CostCenterApprovers" } } },
                new
                {
                    upToAmount = (decimal?)null,
                    steps = new object[]
                    {
                        new { name = "Cost center manager", kind = "CostCenterApprovers" },
                        new { name = "Director", kind = "Group", groupId = (Guid?)directors },
                    },
                },
            },
        });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        return flow;
    }

    /// <summary>
    /// Runs an RFQ where the first supplier quotes 3,900 per laptop and 500 per dock and wins both lines.
    /// The draft PO is 44,000 SAR net, 50,600 SAR with VAT.
    /// </summary>
    public async Task<Guid> DraftPurchaseOrderAsync()
    {
        var rfqId = await Sourcing.PublishedRfqAsync();
        (await SourcingFlow.QuoteAsync(Supplier.User, rfqId, 3_900m, 500m)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var travel = Factory.Clock.Travel(TimeSpan.FromDays(5));
        var award = await Buyer.PostAsJsonAsync($"/api/v1/rfqs/{rfqId}/award", new
        {
            lines = new[] { new { itemLineNumber = 1, supplierId = Supplier.Id }, new { itemLineNumber = 2, supplierId = Supplier.Id } },
        });
        return (await award.ShouldBeOkAsync<AwardBody>()).PurchaseOrderIds.Single();
    }

    /// <summary>A PO that was submitted, approved by the manager and sent to the supplier.</summary>
    public async Task<Guid> SentPurchaseOrderAsync()
    {
        var id = await DraftPurchaseOrderAsync();
        await ApprovedAsync(id);
        (await Buyer.PostAsync($"/api/v1/purchase-orders/{id}/send", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return id;
    }

    public async Task ApprovedAsync(Guid id)
    {
        var submit = await Buyer.PostAsync($"/api/v1/purchase-orders/{id}/submit", null);
        submit.StatusCode.Should().Be(HttpStatusCode.NoContent, await submit.Content.ReadAsStringAsync());
        await ApprovalFlow.ApproveAsync(Manager, (await GetAsync(id)).ApprovalRequestId!.Value);
    }

    public async Task<PurchaseOrderDto> GetAsync(Guid id) =>
        await (await Buyer.GetAsync($"/api/v1/purchase-orders/{id}")).ShouldBeOkAsync<PurchaseOrderDto>();

    public async Task<HttpResponseMessage> ReceiveAsync(Guid id, params object[] lines) =>
        await Receiver.PostAsJsonAsync($"/api/v1/purchase-orders/{id}/receipts", new { lines, note = "Delivered to the Riyadh warehouse" });

    /// <summary>A POST or PUT with an If-Match header.</summary>
    public static async Task<HttpResponseMessage> SendWithVersionAsync(HttpClient client, HttpMethod method, string url, object? body, string version)
    {
        using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{version}\""));
        return await client.SendAsync(request);
    }

    private sealed record AwardBody(List<Guid> PurchaseOrderIds);
}
