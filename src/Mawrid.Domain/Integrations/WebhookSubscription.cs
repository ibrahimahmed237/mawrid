using Mawrid.Domain.Common;

namespace Mawrid.Domain.Integrations;

/// <summary>
/// An outside system (usually the ERP) that wants to hear about events. Each call is signed with
/// the subscription's secret (HMAC-SHA256), so the receiver can trust it.
/// </summary>
public sealed class WebhookSubscription : AggregateRoot<Guid>, ITenantOwned
{
    public const int NameMaxLength = 100;
    public const int UrlMaxLength = 500;

    private WebhookSubscription(Guid id, Guid organizationId) : base(id)
    {
        OrganizationId = organizationId;
        Name = null!;
        Url = null!;
        ProtectedSecret = null!;
        EventTypes = [];
    }

    private WebhookSubscription()
    {
        Name = null!;
        Url = null!;
        ProtectedSecret = null!;
        EventTypes = [];
    }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; }

    public string Url { get; private set; }

    /// <summary>The signing secret, encrypted by the infrastructure. The plain secret is shown to the admin once.</summary>
    public string ProtectedSecret { get; private set; }

    public List<string> EventTypes { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<WebhookSubscription> Create(
        Guid organizationId, string name, Uri url, IReadOnlyCollection<string> eventTypes, string protectedSecret)
    {
        var subscription = new WebhookSubscription(Guid.CreateVersion7(), organizationId) { ProtectedSecret = protectedSecret, IsActive = true };
        var result = subscription.Update(name, url, eventTypes, isActive: true);
        return result.IsSuccess ? subscription : result.Error!;
    }

    public Result Update(string name, Uri url, IReadOnlyCollection<string> eventTypes, bool isActive)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length is < 3 or > NameMaxLength)
        {
            return IntegrationErrors.NameInvalid;
        }

        if (!url.IsAbsoluteUri || url.Scheme is not ("https" or "http") || url.OriginalString.Length > UrlMaxLength)
        {
            return IntegrationErrors.UrlInvalid;
        }

        if (eventTypes.Count == 0 || eventTypes.Any(t => !WebhookEventTypes.All.Contains(t)))
        {
            return IntegrationErrors.EventTypesInvalid;
        }

        Name = name;
        Url = url.OriginalString;
        EventTypes = eventTypes.Distinct().Order().ToList();
        IsActive = isActive;
        return Result.Success();
    }

    public void RotateSecret(string protectedSecret) => ProtectedSecret = protectedSecret;

    public bool Wants(string eventType) => IsActive && EventTypes.Contains(eventType);
}

public static class WebhookEventTypes
{
    public const string PurchaseOrderApproved = "purchase_order.approved";
    public const string SupplierInvoiceReleased = "supplier_invoice.released";

    public static IReadOnlySet<string> All { get; } = new HashSet<string> { PurchaseOrderApproved, SupplierInvoiceReleased };
}
