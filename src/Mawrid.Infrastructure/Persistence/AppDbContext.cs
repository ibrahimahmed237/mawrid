using System.Linq.Expressions;
using Mawrid.Application.Common.Persistence;
using Mawrid.Domain.Approvals;
using Mawrid.Domain.Common;
using Mawrid.Domain.Files;
using Mawrid.Domain.Identity;
using Mawrid.Domain.Integrations;
using Mawrid.Domain.Payables;
using Mawrid.Domain.Purchasing;
using Mawrid.Domain.Requisitions;
using Mawrid.Domain.Sourcing;
using Mawrid.Domain.Suppliers;
using Mawrid.Infrastructure.Auditing;
using Mawrid.Infrastructure.Identity;
using Mawrid.Infrastructure.Numbering;
using Mawrid.Infrastructure.Outbox;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the app. It also holds the ASP.NET Core Identity
/// credential tables. Each module adds its entity configurations in this assembly.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, CurrentUserContext currentUser)
    : IdentityUserContext<UserCredential, Guid>(options), IAppDbContext, IDataProtectionKeyContext
{
    /// <summary>Name of the query filter that hides other organizations' data (ADR-0003).</summary>
    public const string TenantFilter = Application.Common.Persistence.QueryFilters.Tenant;

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<CostCenter> CostCenters => Set<CostCenter>();

    public new DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<StoredFile> Files => Set<StoredFile>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<ApprovalPolicy> ApprovalPolicies => Set<ApprovalPolicy>();

    public DbSet<ApproverGroup> ApproverGroups => Set<ApproverGroup>();

    public DbSet<ApprovalDelegation> ApprovalDelegations => Set<ApprovalDelegation>();

    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    public DbSet<PurchaseRequisition> Requisitions => Set<PurchaseRequisition>();

    public DbSet<Rfq> Rfqs => Set<Rfq>();

    public DbSet<Quotation> Quotations => Set<Quotation>();

    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();

    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();

    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    /// <summary>ASP.NET Core Data Protection keys, shared by all app instances (they encrypt webhook secrets).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<Idempotency.IdempotencyRecord> IdempotencyRecords => Set<Idempotency.IdempotencyRecord>();

    /// <summary>Read by the tenant query filter on every query.</summary>
    public Guid? CurrentOrganizationId => currentUser.OrganizationId;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ConfigureIdentityTables(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in builder.Model.GetEntityTypes().Where(t => !t.IsOwned()).ToList())
        {
            var clrType = entityType.ClrType;

            if (typeof(ITenantOwned).IsAssignableFrom(clrType))
            {
                builder.Entity(clrType).HasQueryFilter(TenantFilter, BuildTenantFilter(clrType));
            }

            if (typeof(IHasDomainEvents).IsAssignableFrom(clrType))
            {
                var entity = builder.Entity(clrType);
                entity.Ignore(nameof(AggregateRoot<Guid>.DomainEvents));
                entity.Property(nameof(AggregateRoot<Guid>.RowVersion)).IsRowVersion();
                entity.Property<DateTimeOffset>(AuditFields.CreatedAtUtc);
                entity.Property<Guid?>(AuditFields.CreatedBy);
                entity.Property<DateTimeOffset>(AuditFields.UpdatedAtUtc);
                entity.Property<Guid?>(AuditFields.UpdatedBy);
            }
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money totals use 2 decimals by default. Unit prices override this with 4 (DESIGN 6.8).
        configurationBuilder.Properties<decimal>().HavePrecision(19, 2);
    }

    /// <summary>Builds <c>e => e.OrganizationId == this.CurrentOrganizationId</c> for one entity type.</summary>
    private LambdaExpression BuildTenantFilter(Type entityType)
    {
        var entity = Expression.Parameter(entityType, "e");
        var organizationId = Expression.Convert(Expression.Property(entity, nameof(ITenantOwned.OrganizationId)), typeof(Guid?));
        var current = Expression.Property(Expression.Constant(this), nameof(CurrentOrganizationId));
        return Expression.Lambda(Expression.Equal(organizationId, current), entity);
    }

    private static void ConfigureIdentityTables(ModelBuilder builder)
    {
        builder.Entity<UserCredential>().ToTable("UserCredentials", Schemas.Identity);
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserCredentialClaims", Schemas.Identity);
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserCredentialLogins", Schemas.Identity);
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserCredentialTokens", Schemas.Identity);
    }
}
