using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Events;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Notifications;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Identity;
using Mawrid.Application.Contracts.Payables;
using Mawrid.Application.Contracts.Purchasing;
using Mawrid.Application.Contracts.Suppliers;
using Mawrid.Domain.Common;
using Mawrid.Domain.Payables;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Payables;

/// <summary>Runs the checks again, for example after a fix outside Mawrid. Overrides of the same problem are kept.</summary>
public sealed record RecheckInvoiceCommand(Guid InvoiceId) : ICommand;

internal sealed class RecheckInvoiceCommandHandler(IAppDbContext db, InvoiceChecks checks) : ICommandHandler<RecheckInvoiceCommand>
{
    public async Task<Result> Handle(RecheckInvoiceCommand command, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices.SingleOrDefaultAsync(i => i.Id == command.InvoiceId, cancellationToken);
        if (invoice is null)
        {
            return PayablesErrors.NotFound;
        }

        if (invoice.Status is not (InvoiceStatus.OnHold or InvoiceStatus.Matched))
        {
            return PayablesErrors.InvalidStatusChange(invoice.Status, InvoiceStatus.OnHold);
        }

        await checks.RunAsync(invoice, null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <param name="LineNumber">The PO line of a line-level hold; null for invoice-level holds.</param>
public sealed record OverrideHoldCommand(Guid InvoiceId, string Code, int? LineNumber, string Reason) : ICommand;

internal sealed class OverrideHoldCommandHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock) : ICommandHandler<OverrideHoldCommand>
{
    public async Task<Result> Handle(OverrideHoldCommand command, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices.SingleOrDefaultAsync(i => i.Id == command.InvoiceId, cancellationToken);
        if (invoice is null)
        {
            return PayablesErrors.NotFound;
        }

        var result = invoice.Override(command.Code, command.LineNumber, command.Reason, currentUser.UserId!.Value,
            isAdmin: currentUser.HasRole(Role.Admin), clock.UtcNow);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

/// <summary>Releases a matched invoice for payment and records the invoiced quantities on the PO, in one transaction.</summary>
public sealed record ReleaseInvoiceCommand(Guid InvoiceId) : ICommand;

internal sealed class ReleaseInvoiceCommandHandler(IAppDbContext db, ICurrentUser currentUser, IPurchaseOrderInvoicing purchaseOrders, IClock clock)
    : ICommandHandler<ReleaseInvoiceCommand>
{
    public async Task<Result> Handle(ReleaseInvoiceCommand command, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices.SingleOrDefaultAsync(i => i.Id == command.InvoiceId, cancellationToken);
        if (invoice is null)
        {
            return PayablesErrors.NotFound;
        }

        var released = invoice.Release(currentUser.UserId!.Value, clock.UtcNow);
        if (released.IsFailure)
        {
            return released;
        }

        var recorded = await purchaseOrders.RecordInvoicedAsync(invoice.PurchaseOrderId,
            invoice.Lines.Select(l => (l.PoLineNumber, l.Quantity)).ToList(), cancellationToken);
        if (recorded.IsFailure)
        {
            return recorded;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Rejects an invoice (wrong PO, not ours, duplicate on paper...). The supplier is told the reason.</summary>
public sealed record RejectInvoiceCommand(Guid InvoiceId, string Reason) : ICommand;

internal sealed class RejectInvoiceCommandHandler(IAppDbContext db) : ICommandHandler<RejectInvoiceCommand>
{
    public async Task<Result> Handle(RejectInvoiceCommand command, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices.SingleOrDefaultAsync(i => i.Id == command.InvoiceId, cancellationToken);
        if (invoice is null)
        {
            return PayablesErrors.NotFound;
        }

        var result = invoice.Reject(command.Reason);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

/// <summary>Checks held invoices again when their cause may be fixed: goods arrived, or a bank account was verified.</summary>
internal sealed class InvoiceRecheckTriggers(IAppDbContext db, InvoiceChecks checks) :
    INotificationHandler<GoodsReceivedIntegrationEvent>,
    INotificationHandler<SupplierBankAccountVerifiedIntegrationEvent>
{
    public Task Handle(GoodsReceivedIntegrationEvent notification, CancellationToken cancellationToken) =>
        RecheckAsync(db.SupplierInvoices.Where(i => i.PurchaseOrderId == notification.PurchaseOrderId), cancellationToken);

    public Task Handle(SupplierBankAccountVerifiedIntegrationEvent notification, CancellationToken cancellationToken) =>
        RecheckAsync(db.SupplierInvoices.Where(i => i.SupplierId == notification.SupplierId), cancellationToken);

    private async Task RecheckAsync(IQueryable<SupplierInvoice> invoices, CancellationToken cancellationToken)
    {
        var held = await invoices.Where(i => i.Status == InvoiceStatus.OnHold).OrderBy(i => i.ReceivedAtUtc).ToListAsync(cancellationToken);
        foreach (var invoice in held)
        {
            await checks.RunAsync(invoice, null, cancellationToken);
        }

        if (held.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>Tells AP clerks about held invoices and the supplier about the outcome; publishes releases for the ERP.</summary>
internal sealed class PayablesNotifications(IAppDbContext db, IOrganizationDirectory directory, INotificationSender notifications, IPublisher publisher,
    IPurchaseOrderInvoicing purchaseOrders) :
    INotificationHandler<DomainEventNotification<SupplierInvoiceHeld>>,
    INotificationHandler<DomainEventNotification<SupplierInvoiceRejected>>,
    INotificationHandler<DomainEventNotification<SupplierInvoiceReleased>>
{
    public async Task Handle(DomainEventNotification<SupplierInvoiceHeld> notification, CancellationToken cancellationToken)
    {
        var invoice = await FindAsync(notification.Event.InvoiceId, cancellationToken);
        if (invoice is not { Status: InvoiceStatus.OnHold })
        {
            return;
        }

        var clerks = await directory.UsersWithRoleAsync(Role.ApClerk, cancellationToken);
        await notifications.SendAsync(clerks, new UserNotification("invoice.on_hold",
            $"Invoice {invoice.SupplierInvoiceNumber} on hold",
            $"Invoice {invoice.SupplierInvoiceNumber} needs attention: {string.Join(", ", notification.Event.HoldCodes)}.",
            $"/api/v1/invoices/{invoice.Id}"), cancellationToken);
    }

    public async Task Handle(DomainEventNotification<SupplierInvoiceRejected> notification, CancellationToken cancellationToken)
    {
        var invoice = await FindAsync(notification.Event.InvoiceId, cancellationToken);
        if (invoice is not null)
        {
            await NotifySupplierAsync(invoice, "invoice.rejected", $"Invoice {invoice.SupplierInvoiceNumber} rejected",
                $"Invoice {invoice.SupplierInvoiceNumber} was rejected: {invoice.RejectionReason}", cancellationToken);
        }
    }

    public async Task Handle(DomainEventNotification<SupplierInvoiceReleased> notification, CancellationToken cancellationToken)
    {
        var invoice = await FindAsync(notification.Event.InvoiceId, cancellationToken);
        if (invoice is null)
        {
            return;
        }

        await NotifySupplierAsync(invoice, "invoice.released", $"Invoice {invoice.SupplierInvoiceNumber} approved for payment",
            $"Invoice {invoice.SupplierInvoiceNumber} ({invoice.Total:N2} SAR) was released for payment.", cancellationToken);

        var order = await purchaseOrders.FindAsync(invoice.PurchaseOrderId, cancellationToken);
        await publisher.Publish(new SupplierInvoiceReleasedIntegrationEvent(
            invoice.OrganizationId, invoice.Id, invoice.SupplierId, invoice.SupplierInvoiceNumber, invoice.IssueDate,
            invoice.PurchaseOrderId, order?.Number ?? string.Empty, invoice.NetTotal, invoice.VatTotal, invoice.Total), cancellationToken);
    }

    private Task<SupplierInvoice?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.SupplierInvoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);

    private async Task NotifySupplierAsync(SupplierInvoice invoice, string type, string title, string message, CancellationToken cancellationToken)
    {
        var users = await directory.SupplierUsersAsync([invoice.SupplierId], cancellationToken);
        await notifications.SendAsync(users, new UserNotification(type, title, message, $"/api/v1/supplier/invoices/{invoice.Id}"), cancellationToken);
    }
}
