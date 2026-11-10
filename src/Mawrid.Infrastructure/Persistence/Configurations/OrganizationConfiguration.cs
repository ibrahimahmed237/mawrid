using Mawrid.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mawrid.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations", Schemas.Identity);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Name).HasMaxLength(Organization.NameMaxLength).IsRequired();

        builder.ComplexProperty(o => o.Settings, settings =>
        {
            settings.Property(s => s.DirectPurchaseLimit).HasColumnName("DirectPurchaseLimit");
            settings.Property(s => s.MinimumSuppliersPerRfq).HasColumnName("MinimumSuppliersPerRfq");
            settings.Property(s => s.MinimumRfqWindowDays).HasColumnName("MinimumRfqWindowDays");
            settings.Property(s => s.PriceTolerancePercent).HasColumnName("PriceTolerancePercent").HasPrecision(5, 2);
            settings.Property(s => s.PriceToleranceMaxAmount).HasColumnName("PriceToleranceMaxAmount");
            settings.Property(s => s.OverDeliveryPercent).HasColumnName("OverDeliveryPercent").HasPrecision(5, 2);
        });
    }
}
