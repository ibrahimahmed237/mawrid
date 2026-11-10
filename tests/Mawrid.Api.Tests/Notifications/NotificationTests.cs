using System.Net;
using System.Net.Http.Json;
using Mawrid.Api.Notifications;
using Mawrid.Api.Tests.Infrastructure;
using Mawrid.Application.Common.Notifications;
using Mawrid.Application.Identity.Users;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Mawrid.Api.Tests.Notifications;

[Collection(ApiCollection.Name)]
public sealed class NotificationTests(MawridApiFactory factory)
{
    [Fact]
    public async Task The_supplier_gets_a_push_and_an_email_when_a_po_is_sent()
    {
        var flow = await PurchasingFlow.StartAsync(factory);
        var id = await flow.DraftPurchaseOrderAsync();
        await flow.ApprovedAsync(id);
        var number = (await flow.GetAsync(id)).Number;

        var received = new TaskCompletionSource<UserNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = Connect(flow.Supplier.User);
        connection.On<UserNotification>(NotificationsHub.ClientMethod, n =>
        {
            if (n.Type == "purchase_order.sent")
            {
                received.TrySetResult(n);
            }
        });
        await connection.StartAsync();

        (await flow.Buyer.PostAsync($"/api/v1/purchase-orders/{id}/send", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await factory.DeliverOutboxAsync();

        var push = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        push.Title.Should().Contain(number);
        push.Path.Should().Be($"/api/v1/supplier/purchase-orders/{id}");

        var supplierEmail = (await MeAsync(flow.Supplier.User)).Email;
        factory.Emails.To(supplierEmail).Should().Contain(e => e.Subject.Contains(number));
    }

    [Fact]
    public async Task Approvers_are_told_when_a_step_waits_and_the_requester_when_it_ends()
    {
        var flow = await PurchasingFlow.StartAsync(factory);
        var id = await flow.DraftPurchaseOrderAsync();
        (await flow.Buyer.PostAsync($"/api/v1/purchase-orders/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await factory.DeliverOutboxAsync();

        var po = await flow.GetAsync(id);
        var managerEmail = (await MeAsync(flow.Manager)).Email;
        factory.Emails.To(managerEmail).Should().Contain(e => e.Subject.StartsWith("Approval needed") && e.Subject.Contains(po.Number));

        await ApprovalFlow.ApproveAsync(flow.Manager, po.ApprovalRequestId!.Value);
        await factory.DeliverOutboxAsync();

        var buyerEmail = (await MeAsync(flow.Buyer)).Email;
        factory.Emails.To(buyerEmail).Should().Contain(e => e.Subject.StartsWith("Approved") && e.Subject.Contains(po.Number));
    }

    [Fact]
    public async Task Invited_suppliers_hear_about_a_new_rfq_and_losers_hear_about_the_award()
    {
        var flow = await PurchasingFlow.StartAsync(factory);
        await flow.DraftPurchaseOrderAsync();

        // The award happened 5 days ahead on the test clock; its events are due from then.
        using (factory.Clock.Travel(TimeSpan.FromDays(5)))
        {
            await factory.DeliverOutboxAsync();
        }

        var winner = (await MeAsync(flow.Supplier.User)).Email;
        var loser = (await MeAsync(flow.Sourcing.ApprovedSuppliers[1].User)).Email;
        factory.Emails.To(loser).Should().Contain(e => e.Subject.StartsWith("New RFQ"));
        factory.Emails.To(winner).Should().Contain(e => e.Subject.EndsWith("you won"));
        factory.Emails.To(loser).Should().Contain(e => e.Body.Contains("awarded to another supplier"));
    }

    [Fact]
    public async Task The_hub_refuses_anonymous_connections()
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, NotificationsHub.Path), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        var start = () => connection.StartAsync();

        (await start.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Connects with the client's access token. Long polling works with the in-memory test server.</summary>
    private HubConnection Connect(HttpClient client)
    {
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        return new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, NotificationsHub.Path), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
    }

    private static async Task<UserDto> MeAsync(HttpClient client) =>
        await (await client.GetAsync("/api/v1/me")).ShouldBeOkAsync<UserDto>();
}
