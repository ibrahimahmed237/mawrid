using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Events;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Notifications;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Identity;
using Mawrid.Domain.Common;
using Mawrid.Domain.Suppliers;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Suppliers;

/// <summary>
/// System job, daily per organization: warns about supplier documents that expire in 30 or 7 days,
/// or expired yesterday. An expired document stops the supplier from winning awards.
/// </summary>
public sealed record WarnExpiringSupplierDocumentsCommand : ICommand<int>;

internal sealed class WarnExpiringSupplierDocumentsCommandHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<WarnExpiringSupplierDocumentsCommand, int>
{
    public async Task<Result<int>> Handle(WarnExpiringSupplierDocumentsCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsSystem)
        {
            return SystemErrors.SystemOnly;
        }

        var today = clock.Today;
        var days = Supplier.ExpiryWarningDays.Select(d => today.AddDays(d)).ToList();
        var suppliers = await db.Suppliers
            .Where(s => s.Status == SupplierStatus.Approved && s.Documents.Any(d => days.Contains(d.ExpiresOn)))
            .ToListAsync(cancellationToken);

        var warnings = suppliers.Sum(s => s.WarnAboutExpiringDocuments(today));
        await db.SaveChangesAsync(cancellationToken);
        return warnings;
    }
}

/// <summary>Tells the supplier's users and the buyers that a document expires.</summary>
internal sealed class SupplierDocumentExpiryNotifications(IAppDbContext db, IOrganizationDirectory directory, INotificationSender notifications)
    : INotificationHandler<DomainEventNotification<SupplierDocumentExpiring>>
{
    public async Task Handle(DomainEventNotification<SupplierDocumentExpiring> notification, CancellationToken cancellationToken)
    {
        var e = notification.Event;
        var supplier = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(s => s.Id == e.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return;
        }

        var recipients = (await directory.SupplierUsersAsync([supplier.Id], cancellationToken))
            .Concat(await directory.UsersWithRoleAsync(Role.Buyer, cancellationToken))
            .ToList();

        var (title, message) = e.DaysLeft < 0
            ? ($"{supplier.NameEn}: {e.DocumentType} expired",
                $"The {e.DocumentType} of {supplier.NameEn} expired on {e.ExpiresOn:yyyy-MM-dd}. The supplier cannot win awards until a new one is uploaded.")
            : ($"{supplier.NameEn}: {e.DocumentType} expires in {e.DaysLeft} days",
                $"The {e.DocumentType} of {supplier.NameEn} expires on {e.ExpiresOn:yyyy-MM-dd}. Please upload a new one before then.");

        await notifications.SendAsync(recipients, new UserNotification("supplier.document_expiring", title, message, $"/api/v1/suppliers/{supplier.Id}"),
            cancellationToken);
    }
}
