using Mawrid.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mawrid.Infrastructure.Persistence.Configurations;

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("PurchaseOrders", Schemas.Purchasing);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Number).HasMaxLength(30).IsRequired();
        builder.Property(o => o.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.RejectionReason).HasMaxLength(500);
        builder.Property(o => o.CancellationReason).HasMaxLength(PurchaseOrder.ReasonMaxLength);
        builder.Property(o => o.ClosedReason).HasMaxLength(PurchaseOrder.ReasonMaxLength);
        builder.Property(o => o.DeliveryAddress).HasMaxLength(PurchaseOrder.DeliveryAddressMaxLength);
        builder.PrimitiveCollection(o => o.RequisitionIds).IsRequired();
        builder.HasIndex(o => new { o.OrganizationId, o.Number }).IsUnique();
        builder.HasIndex(o => new { o.OrganizationId, o.SupplierId, o.Status });
        builder.Ignore(o => o.NetTotal);
        builder.Ignore(o => o.VatTotal);
        builder.Ignore(o => o.Total);

        builder.OwnsMany(o => o.Lines, lines =>
        {
            lines.ToTable("PurchaseOrderLines", Schemas.Purchasing);
            lines.WithOwner().HasForeignKey("PurchaseOrderId");
            lines.HasKey("PurchaseOrderId", nameof(PurchaseOrderLine.LineNumber));
            lines.Property(l => l.LineNumber).ValueGeneratedNever();
            lines.Property(l => l.Description).HasMaxLength(500).IsRequired();
            lines.Property(l => l.Unit).HasMaxLength(20).IsRequired();
            lines.Property(l => l.Quantity).HasPrecision(18, 3);
            lines.Property(l => l.UnitPrice).HasPrecision(19, 4);
            lines.Property(l => l.VatCategory).HasConversion<string>().HasMaxLength(20);
            lines.Property(l => l.VatRate).HasPrecision(5, 4);
            lines.Property(l => l.AcceptedQuantity).HasPrecision(18, 3);
            lines.Property(l => l.RejectedQuantity).HasPrecision(18, 3);
            lines.Property(l => l.InvoicedQuantity).HasPrecision(18, 3);
        });
        builder.Navigation(o => o.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(o => o.Receipts, receipts =>
        {
            receipts.ToTable("GoodsReceipts", Schemas.Purchasing);
            receipts.WithOwner().HasForeignKey("PurchaseOrderId");
            receipts.HasKey(r => r.Id);
            receipts.Property(r => r.Id).ValueGeneratedNever();
            receipts.Property(r => r.Note).HasMaxLength(500);
            receipts.Property(r => r.Lines).HasJsonConversion().HasColumnName("Lines").IsRequired();
        });
        builder.Navigation(o => o.Receipts).HasField("_receipts").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
