using Mawrid.Domain.Payables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mawrid.Infrastructure.Persistence.Configurations;

internal sealed class SupplierInvoiceConfiguration : IEntityTypeConfiguration<SupplierInvoice>
{
    public void Configure(EntityTypeBuilder<SupplierInvoice> builder)
    {
        builder.ToTable("SupplierInvoices", Schemas.Payables);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.SupplierInvoiceNumber).HasMaxLength(SupplierInvoice.NumberMaxLength).IsRequired();
        builder.Property(i => i.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(i => i.QrCode).HasMaxLength(ZatcaQr.MaxLength);
        builder.Property(i => i.RejectionReason).HasMaxLength(SupplierInvoice.ReasonMaxLength);

        // The duplicate check: one invoice number per supplier (DESIGN 6.7).
        builder.HasIndex(i => new { i.OrganizationId, i.SupplierId, i.SupplierInvoiceNumber }).IsUnique();
        builder.HasIndex(i => new { i.OrganizationId, i.Status });
        builder.HasIndex(i => new { i.OrganizationId, i.PurchaseOrderId });
        builder.Ignore(i => i.OpenHolds);

        builder.OwnsMany(i => i.Lines, lines =>
        {
            lines.ToTable("SupplierInvoiceLines", Schemas.Payables);
            lines.WithOwner().HasForeignKey("SupplierInvoiceId");
            lines.HasKey(l => l.Id);
            lines.Property(l => l.Id).ValueGeneratedNever();
            lines.Property(l => l.Description).HasMaxLength(SupplierInvoiceLine.DescriptionMaxLength).IsRequired();
            lines.Property(l => l.Quantity).HasPrecision(18, 3);
            lines.Property(l => l.UnitPrice).HasPrecision(19, 4);
            lines.Property(l => l.VatCategory).HasConversion<string>().HasMaxLength(20);
        });
        builder.Navigation(i => i.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(i => i.Holds, holds =>
        {
            holds.ToTable("SupplierInvoiceHolds", Schemas.Payables);
            holds.WithOwner().HasForeignKey("SupplierInvoiceId");
            holds.HasKey(h => h.Id);
            holds.Property(h => h.Id).ValueGeneratedNever();
            holds.Property(h => h.Code).HasMaxLength(50).IsRequired();
            holds.Property(h => h.Expected).HasMaxLength(200).IsRequired();
            holds.Property(h => h.Actual).HasMaxLength(200).IsRequired();
            holds.Property(h => h.OverrideReason).HasMaxLength(SupplierInvoice.ReasonMaxLength);
            holds.Ignore(h => h.IsOverridden);
        });
        builder.Navigation(i => i.Holds).HasField("_holds").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
