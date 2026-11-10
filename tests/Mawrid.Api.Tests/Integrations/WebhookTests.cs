using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mawrid.Api.Tests.Infrastructure;
using Mawrid.Application.Integrations;

namespace Mawrid.Api.Tests.Integrations;

[Collection(ApiCollection.Name)]
public sealed class WebhookTests(MawridApiFactory factory)
{
    private const string Secret = "whsec_integration-tests-0123456789abcdef";

    [Fact]
    public async Task A_released_invoice_reaches_the_erp_with_a_valid_signature()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        var url = await SubscribeAsync(flow.Admin, "supplier_invoice.released");
        var invoiceId = await flow.SubmittedInvoiceAsync(flow.MatchingInvoice("INV-W1"));
        (await flow.ApClerk.PostAsync($"/api/v1/invoices/{invoiceId}/release", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await factory.DeliverOutboxAsync();
        (await factory.RunAsSystemAsync(flow.Purchasing.Sourcing.Approvals.OrganizationId, new DeliverWebhooksCommand())).Value.Should().Be(1);

        var call = factory.Webhooks.For(url).Should().ContainSingle().Subject;
        call.Headers["X-Mawrid-Event"].Should().Be("supplier_invoice.released");
        WebhookSignature.Verify(Secret, long.Parse(call.Headers["X-Mawrid-Timestamp"]), call.Body, call.Headers["X-Mawrid-Signature"]).Should().BeTrue();

        using var body = JsonDocument.Parse(call.Body);
        body.RootElement.GetProperty("type").GetString().Should().Be("supplier_invoice.released");
        body.RootElement.GetProperty("data").GetProperty("supplierInvoiceNumber").GetString().Should().Be("INV-W1");
        body.RootElement.GetProperty("data").GetProperty("total").GetDecimal().Should().Be(32_660m);

        var log = await (await flow.Admin.GetAsync("/api/v1/webhook-deliveries?status=Succeeded")).ShouldBeOkAsync<DeliveryPage>();
        log.Items.Should().Contain(d => d.Id == Guid.Parse(call.Headers["X-Mawrid-Delivery"]) && d.LastStatusCode == 204);
    }

    [Fact]
    public async Task An_approved_po_reaches_the_erp_with_its_lines()
    {
        var flow = await PurchasingFlow.StartAsync(factory);
        var url = await SubscribeAsync(flow.Sourcing.Approvals.Admin, "purchase_order.approved");
        var poId = await flow.DraftPurchaseOrderAsync();
        await flow.ApprovedAsync(poId);

        await factory.DeliverOutboxAsync();
        await factory.RunAsSystemAsync(flow.Sourcing.Approvals.OrganizationId, new DeliverWebhooksCommand());

        var call = factory.Webhooks.For(url).Should().ContainSingle().Subject;
        using var body = JsonDocument.Parse(call.Body);
        var data = body.RootElement.GetProperty("data");
        data.GetProperty("number").GetString().Should().Be((await flow.GetAsync(poId)).Number);
        data.GetProperty("total").GetDecimal().Should().Be(50_600m);
        data.GetProperty("lines").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task A_failing_erp_is_retried_later_then_marked_failed_and_can_be_replayed()
    {
        var flow = await PayablesFlow.StartAsync(factory);
        var url = await SubscribeAsync(flow.Admin, "supplier_invoice.released");
        factory.Webhooks.StatusByUrl[url] = HttpStatusCode.InternalServerError;
        var invoiceId = await flow.SubmittedInvoiceAsync(flow.MatchingInvoice("INV-W2"));
        (await flow.ApClerk.PostAsync($"/api/v1/invoices/{invoiceId}/release", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await factory.DeliverOutboxAsync();
        var organizationId = flow.Purchasing.Sourcing.Approvals.OrganizationId;

        // Each run is far enough after the previous one for the next attempt to be due.
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            using (factory.Clock.Travel(TimeSpan.FromHours(6 * attempt)))
            {
                await factory.RunAsSystemAsync(organizationId, new DeliverWebhooksCommand());
            }
        }

        factory.Webhooks.For(url).Should().HaveCount(8);
        var failed = (await (await flow.Admin.GetAsync("/api/v1/webhook-deliveries?status=Failed")).ShouldBeOkAsync<DeliveryPage>())
            .Items.Single(d => d.SubscriptionId == SubscriptionIdOf(url));
        failed.Attempts.Should().Be(8);
        failed.LastError.Should().Be("HTTP 500 Internal Server Error");

        // The ERP is fixed: replay sends the same body again.
        factory.Webhooks.StatusByUrl[url] = HttpStatusCode.OK;
        (await flow.Admin.PostAsync($"/api/v1/webhook-deliveries/{failed.Id}/replay", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await factory.RunAsSystemAsync(organizationId, new DeliverWebhooksCommand())).Value.Should().Be(1);

        factory.Webhooks.For(url).Select(c => c.Body).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Subscriptions_need_https_admins_and_never_show_the_secret_again()
    {
        var flow = await ApprovalFlow.StartAsync(factory, withPolicy: false);

        var http = await flow.Admin.PostAsJsonAsync("/api/v1/webhook-subscriptions",
            new { name = "ERP", url = "http://erp.test/hooks", eventTypes = new[] { "purchase_order.approved" } });
        await http.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "webhook.url_invalid");

        var byBuyer = await flow.Buyer.PostAsJsonAsync("/api/v1/webhook-subscriptions",
            new { name = "ERP", url = "https://erp.test/hooks", eventTypes = new[] { "purchase_order.approved" } });
        byBuyer.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var created = await (await flow.Admin.PostAsJsonAsync("/api/v1/webhook-subscriptions",
            new { name = "ERP", url = "https://erp.test/hooks", eventTypes = new[] { "purchase_order.approved" } }))
            .ShouldBeOkAsync<WebhookSecretDto>(HttpStatusCode.Created);
        created.Secret.Should().StartWith("whsec_");

        var list = await (await flow.Admin.GetAsync("/api/v1/webhook-subscriptions")).Content.ReadAsStringAsync();
        list.Should().Contain("https://erp.test/hooks").And.NotContain(created.Secret).And.NotContain("secret", "no secret field is returned");

        var rotated = await (await flow.Admin.PostAsync($"/api/v1/webhook-subscriptions/{created.Id}/rotate-secret", null)).ShouldBeOkAsync<WebhookSecretDto>();
        rotated.Secret.Should().NotBe(created.Secret);
    }

    private readonly Dictionary<string, Guid> _subscriptions = [];

    /// <summary>Creates a subscription with a known secret and a unique URL, so tests do not see each other's calls.</summary>
    private async Task<string> SubscribeAsync(HttpClient admin, string eventType)
    {
        var url = $"https://erp.test/hooks/{Guid.NewGuid():N}";
        var created = await (await admin.PostAsJsonAsync("/api/v1/webhook-subscriptions", new { name = "Test ERP", url, eventTypes = new[] { eventType }, secret = Secret }))
            .ShouldBeOkAsync<WebhookSecretDto>(HttpStatusCode.Created);
        created.Secret.Should().Be(Secret);
        _subscriptions[url] = created.Id;
        return url;
    }

    private Guid SubscriptionIdOf(string url) => _subscriptions[url];

    private sealed record DeliveryPage(List<WebhookDeliveryDto> Items);
}
