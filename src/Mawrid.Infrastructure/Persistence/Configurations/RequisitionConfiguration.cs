using Mawrid.Domain.Requisitions;
using Mawrid.Infrastructure.Numbering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mawrid.Infrastructure.Persistence.Configurations;

internal sealed class RequisitionConfiguration : IEntityTypeConfiguration<PurchaseRequisition>
{
    public void Configure(EntityTypeBuilder<PurchaseRequisition> builder)
    {
        builder.ToTable("PurchaseRequisitions", Schemas.Requisitions);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Number).HasMaxLength(30).IsRequired();
        builder.Property(r => r.Title).HasMaxLength(PurchaseRequisition.TitleMaxLength).IsRequired();
        builder.Property(r => r.Justification).HasMaxLength(PurchaseRequisition.JustificationMaxLength).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.SourcingRoute).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.RejectionReason).HasMaxLength(500);
        builder.Property(r => r.SingleSourceReason).HasMaxLength(500);
        builder.HasIndex(r => new { r.OrganizationId, r.Number }).IsUnique();
        builder.HasIndex(r => new { r.OrganizationId, r.RequesterId });
        builder.Ignore(r => r.EstimatedNetTotal);
        builder.Ignore(r => r.EstimatedTotalWithVat);

        builder.OwnsMany(r => r.Lines, lines =>
        {
            lines.ToTable("RequisitionLines", Schemas.Requisitions);
            lines.WithOwner().HasForeignKey("RequisitionId");
            lines.HasKey("RequisitionId", nameof(RequisitionLine.LineNumber));
            lines.Property(l => l.LineNumber).ValueGeneratedNever();
            lines.Property(l => l.Description).HasMaxLength(RequisitionLine.DescriptionMaxLength).IsRequired();
            lines.Property(l => l.Unit).HasMaxLength(RequisitionLine.UnitMaxLength).IsRequired();
            lines.Property(l => l.Quantity).HasPrecision(18, 3);
            lines.Property(l => l.EstimatedUnitPrice).HasPrecision(19, 4);
            lines.Ignore(l => l.EstimatedNetAmount);
        });
        builder.Navigation(r => r.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class DocumentSequenceConfiguration : IEntityTypeConfiguration<DocumentSequence>
{
    public void Configure(EntityTypeBuilder<DocumentSequence> builder)
    {
        builder.ToTable("DocumentSequences", Schemas.Platform);
        builder.HasKey(s => new { s.OrganizationId, s.Prefix, s.Year });
        builder.Property(s => s.Prefix).HasMaxLength(10);
    }
}
