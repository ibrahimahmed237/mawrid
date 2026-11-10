using Mawrid.Domain.Integrations;

namespace Mawrid.Domain.Tests.Integrations;

public class WebhookTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private static WebhookDelivery NewDelivery() =>
        WebhookDelivery.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), WebhookEventTypes.PurchaseOrderApproved, "{}", Now);

    [Fact]
    public void A_failed_call_is_retried_later_with_a_growing_wait()
    {
        var delivery = NewDelivery();

        delivery.RecordFailure(500, "Internal Server Error", Now);
        delivery.NextAttemptAtUtc.Should().Be(Now.AddMinutes(1));
        delivery.RecordFailure(null, "Timeout", Now);
        delivery.NextAttemptAtUtc.Should().Be(Now.AddMinutes(2));

        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
        delivery.Attempts.Should().Be(2);
        delivery.LastStatusCode.Should().BeNull();
    }

    [Fact]
    public void After_the_last_attempt_the_delivery_fails_and_a_replay_starts_again()
    {
        var delivery = NewDelivery();
        for (var i = 0; i < WebhookDelivery.MaxAttempts; i++)
        {
            delivery.RecordFailure(503, "Service Unavailable", Now);
        }

        delivery.Status.Should().Be(WebhookDeliveryStatus.Failed);

        delivery.Replay(Now.AddDays(1));
        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
        delivery.Attempts.Should().Be(0);
        delivery.NextAttemptAtUtc.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void Success_records_when_it_was_delivered()
    {
        var delivery = NewDelivery();
        delivery.RecordFailure(500, "x", Now);

        delivery.RecordSuccess(204, Now.AddMinutes(1));

        delivery.Status.Should().Be(WebhookDeliveryStatus.Succeeded);
        delivery.DeliveredAtUtc.Should().Be(Now.AddMinutes(1));
        delivery.LastError.Should().BeNull();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 8)]
    [InlineData(20, 360)]
    public void Backoff_doubles_up_to_six_hours(int attempts, int minutes)
    {
        WebhookDelivery.Backoff(attempts).Should().Be(TimeSpan.FromMinutes(minutes));
    }

    [Fact]
    public void A_subscription_needs_a_known_event_type_and_an_absolute_url()
    {
        var org = Guid.NewGuid();

        WebhookSubscription.Create(org, "ERP", new Uri("https://erp.example.sa/hooks"), ["order.shipped"], "x")
            .Error.Should().Be(IntegrationErrors.EventTypesInvalid);
        WebhookSubscription.Create(org, "ERP", new Uri("ftp://erp.example.sa/hooks"), [WebhookEventTypes.PurchaseOrderApproved], "x")
            .Error.Should().Be(IntegrationErrors.UrlInvalid);

        var subscription = WebhookSubscription.Create(org, "ERP", new Uri("https://erp.example.sa/hooks"),
            [WebhookEventTypes.SupplierInvoiceReleased, WebhookEventTypes.SupplierInvoiceReleased], "x").Value;
        subscription.EventTypes.Should().Equal(WebhookEventTypes.SupplierInvoiceReleased);
        subscription.Wants(WebhookEventTypes.SupplierInvoiceReleased).Should().BeTrue();
        subscription.Wants(WebhookEventTypes.PurchaseOrderApproved).Should().BeFalse();
    }
}
