using System.Text.Json;
using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Paging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Payables;
using Mawrid.Application.Contracts.Purchasing;
using Mawrid.Domain.Common;
using Mawrid.Domain.Integrations;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Integrations;

/// <summary>Queues a delivery for every active subscription that wants the event. The job sends them.</summary>
internal sealed class WebhookEventQueue(IAppDbContext db, IClock clock) :
    INotificationHandler<PurchaseOrderApprovedIntegrationEvent>,
    INotificationHandler<SupplierInvoiceReleasedIntegrationEvent>
{
    public Task Handle(PurchaseOrderApprovedIntegrationEvent notification, CancellationToken cancellationToken) =>
        QueueAsync(notification.OrganizationId, WebhookEventTypes.PurchaseOrderApproved, new
        {
            purchaseOrderId = notification.PurchaseOrderId,
            number = notification.Number,
            supplierId = notification.SupplierId,
            costCenterId = notification.CostCenterId,
            paymentTermsDays = notification.PaymentTermsDays,
            currency = "SAR",
            netTotal = notification.NetTotal,
            vatTotal = notification.VatTotal,
            total = notification.Total,
            lines = notification.Lines,
        }, cancellationToken);

    public Task Handle(SupplierInvoiceReleasedIntegrationEvent notification, CancellationToken cancellationToken) =>
        QueueAsync(notification.OrganizationId, WebhookEventTypes.SupplierInvoiceReleased, new
        {
            invoiceId = notification.InvoiceId,
            supplierId = notification.SupplierId,
            supplierInvoiceNumber = notification.SupplierInvoiceNumber,
            issueDate = notification.IssueDate,
            purchaseOrderId = notification.PurchaseOrderId,
            purchaseOrderNumber = notification.PurchaseOrderNumber,
            currency = "SAR",
            netTotal = notification.NetTotal,
            vatTotal = notification.VatTotal,
            total = notification.Total,
        }, cancellationToken);

    private async Task QueueAsync(Guid organizationId, string eventType, object data, CancellationToken cancellationToken)
    {
        var subscriptions = (await db.WebhookSubscriptions.AsNoTracking().Where(s => s.IsActive).ToListAsync(cancellationToken))
            .Where(s => s.Wants(eventType))
            .ToList();
        if (subscriptions.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        var eventId = Guid.CreateVersion7();
        var payload = JsonSerializer.Serialize(new { id = eventId, type = eventType, occurredAtUtc = now, data }, JsonSerializerOptions.Web);
        foreach (var subscription in subscriptions)
        {
            db.WebhookDeliveries.Add(WebhookDelivery.Create(Guid.CreateVersion7(), organizationId, subscription.Id, eventType, payload, now));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>System job, every minute per organization: sends the deliveries that are due.</summary>
public sealed record DeliverWebhooksCommand : ICommand<int>;

internal sealed class DeliverWebhooksCommandHandler(IAppDbContext db, ICurrentUser currentUser, IWebhookSender sender, IWebhookSecrets secrets, IClock clock)
    : ICommandHandler<DeliverWebhooksCommand, int>
{
    private const int BatchSize = 50;

    public async Task<Result<int>> Handle(DeliverWebhooksCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsSystem)
        {
            return SystemErrors.SystemOnly;
        }

        var now = clock.UtcNow;
        var due = await db.WebhookDeliveries
            .Where(d => d.Status == WebhookDeliveryStatus.Pending && d.NextAttemptAtUtc <= now)
            .OrderBy(d => d.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        var subscriptionIds = due.Select(d => d.SubscriptionId).Distinct().ToList();
        var subscriptions = await db.WebhookSubscriptions.AsNoTracking().Where(s => subscriptionIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);

        var delivered = 0;
        foreach (var delivery in due)
        {
            if (!subscriptions.TryGetValue(delivery.SubscriptionId, out var subscription) || !subscription.IsActive)
            {
                delivery.RecordFailure(null, "The subscription is inactive.", now);
                continue;
            }

            var result = await sender.SendAsync(new WebhookRequest(new Uri(subscription.Url), delivery.EventType, delivery.Id, delivery.Payload,
                secrets.Unprotect(subscription.ProtectedSecret), clock.UtcNow), cancellationToken);
            if (result.Success)
            {
                delivery.RecordSuccess(result.StatusCode!.Value, clock.UtcNow);
                delivered++;
            }
            else
            {
                delivery.RecordFailure(result.StatusCode, result.Error ?? "Unknown error", clock.UtcNow);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return delivered;
    }
}

public sealed record WebhookDeliveryDto(
    Guid Id, Guid SubscriptionId, string EventType, string Status, int Attempts, DateTimeOffset CreatedAtUtc, DateTimeOffset NextAttemptAtUtc,
    DateTimeOffset? DeliveredAtUtc, int? LastStatusCode, string? LastError, string Payload);

/// <summary>The delivery log, newest first. Filter by status to find failures.</summary>
public sealed record GetWebhookDeliveriesQuery(PageRequest Page, string? Status = null) : IQuery<PagedList<WebhookDeliveryDto>>;

internal sealed class GetWebhookDeliveriesQueryValidator : AbstractValidator<GetWebhookDeliveriesQuery>
{
    public GetWebhookDeliveriesQueryValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.Status).Must(s => s is null || Enum.TryParse<WebhookDeliveryStatus>(s, ignoreCase: true, out _));
    }
}

internal sealed class GetWebhookDeliveriesQueryHandler(IAppDbContext db) : IQueryHandler<GetWebhookDeliveriesQuery, PagedList<WebhookDeliveryDto>>
{
    public async Task<Result<PagedList<WebhookDeliveryDto>>> Handle(GetWebhookDeliveriesQuery query, CancellationToken cancellationToken)
    {
        var deliveries = db.WebhookDeliveries.AsNoTracking();
        if (query.Status is not null)
        {
            var status = Enum.Parse<WebhookDeliveryStatus>(query.Status, ignoreCase: true);
            deliveries = deliveries.Where(d => d.Status == status);
        }

        var page = await deliveries.OrderByDescending(d => d.CreatedAtUtc).ToPagedListAsync(query.Page, cancellationToken);
        return new PagedList<WebhookDeliveryDto>(
            page.Items.Select(d => new WebhookDeliveryDto(d.Id, d.SubscriptionId, d.EventType, d.Status.ToString(), d.Attempts, d.CreatedAtUtc,
                d.NextAttemptAtUtc, d.DeliveredAtUtc, d.LastStatusCode, d.LastError, d.Payload)).ToList(),
            page.Page, page.PageSize, page.Total);
    }
}

/// <summary>Sends a delivery again with the same body and id, so the receiver can still spot a duplicate.</summary>
public sealed record ReplayWebhookDeliveryCommand(Guid DeliveryId) : ICommand;

internal sealed class ReplayWebhookDeliveryCommandHandler(IAppDbContext db, IClock clock) : ICommandHandler<ReplayWebhookDeliveryCommand>
{
    public async Task<Result> Handle(ReplayWebhookDeliveryCommand command, CancellationToken cancellationToken)
    {
        var delivery = await db.WebhookDeliveries.SingleOrDefaultAsync(d => d.Id == command.DeliveryId, cancellationToken);
        if (delivery is null)
        {
            return IntegrationErrors.DeliveryNotFound;
        }

        delivery.Replay(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
