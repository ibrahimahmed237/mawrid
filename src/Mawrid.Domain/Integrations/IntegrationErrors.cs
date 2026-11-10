using Mawrid.Domain.Common;

namespace Mawrid.Domain.Integrations;

public static class IntegrationErrors
{
    public static readonly Error NameInvalid = Error.Validation("webhook.name_invalid", "The name must be 3 to 100 characters.");

    public static readonly Error UrlInvalid = Error.Validation("webhook.url_invalid", "The URL must be an absolute HTTPS address of at most 500 characters.");

    public static readonly Error EventTypesInvalid = Error.Validation(
        "webhook.event_types_invalid", $"Choose one or more of: {string.Join(", ", WebhookEventTypes.All.Order())}.");

    public static readonly Error SecretInvalid = Error.Validation("webhook.secret_invalid", "A secret you choose must be 32 to 200 characters.");

    public static readonly Error SubscriptionNotFound = Error.NotFound("webhook.not_found", "The webhook subscription was not found.");

    public static readonly Error DeliveryNotFound = Error.NotFound("webhook_delivery.not_found", "The webhook delivery was not found.");
}
