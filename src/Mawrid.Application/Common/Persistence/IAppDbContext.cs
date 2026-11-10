using Mawrid.Domain.Approvals;
using Mawrid.Domain.Files;
using Mawrid.Domain.Identity;
using Mawrid.Domain.Integrations;
using Mawrid.Domain.Payables;
using Mawrid.Domain.Purchasing;
using Mawrid.Domain.Requisitions;
using Mawrid.Domain.Sourcing;
using Mawrid.Domain.Suppliers;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Common.Persistence;

/// <summary>
/// The database as seen by handlers (ADR-0008). Every set of tenant-owned data is
/// already filtered to the current organization.
/// </summary>
public interface IAppDbContext
{
    DbSet<Organization> Organizations { get; }

    DbSet<CostCenter> CostCenters { get; }

    DbSet<User> Users { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<StoredFile> Files { get; }

    DbSet<Supplier> Suppliers { get; }

    DbSet<ApprovalPolicy> ApprovalPolicies { get; }

    DbSet<ApproverGroup> ApproverGroups { get; }

    DbSet<ApprovalDelegation> ApprovalDelegations { get; }

    DbSet<ApprovalRequest> ApprovalRequests { get; }

    DbSet<PurchaseRequisition> Requisitions { get; }

    DbSet<Rfq> Rfqs { get; }

    DbSet<Quotation> Quotations { get; }

    DbSet<PurchaseOrder> PurchaseOrders { get; }

    DbSet<SupplierInvoice> SupplierInvoices { get; }

    DbSet<WebhookSubscription> WebhookSubscriptions { get; }

    DbSet<WebhookDelivery> WebhookDeliveries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
