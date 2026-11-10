using Mawrid.Domain.Sourcing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mawrid.Infrastructure.Persistence.Configurations;

internal sealed class RfqConfiguration : IEntityTypeConfiguration<Rfq>
{
    public void Configure(EntityTypeBuilder<Rfq> builder)
    {
        builder.ToTable("Rfqs", Schemas.Sourcing);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Number).HasMaxLength(30).IsRequired();
        builder.Property(r => r.Title).HasMaxLength(Rfq.TitleMaxLength).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(Rfq.DescriptionMaxLength);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.SingleSourceReason).HasMaxLength(Rfq.ReasonMaxLength);
        builder.Property(r => r.CancellationReason).HasMaxLength(Rfq.ReasonMaxLength);
        builder.HasIndex(r => new { r.OrganizationId, r.Number }).IsUnique();
        builder.HasIndex(r => new { r.Status, r.Deadline });

        builder.OwnsMany(r => r.Items, items =>
        {
            items.ToTable("RfqItems", Schemas.Sourcing);
            items.WithOwner().HasForeignKey("RfqId");
            items.HasKey("RfqId", nameof(RfqItem.LineNumber));
            items.Property(i => i.LineNumber).ValueGeneratedNever();
            items.Property(i => i.Description).HasMaxLength(500).IsRequired();
            items.Property(i => i.Unit).HasMaxLength(20).IsRequired();
            items.Property(i => i.Quantity).HasPrecision(18, 3);
        });
        builder.Navigation(r => r.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(r => r.Invitations, invitations =>
        {
            invitations.ToTable("RfqInvitations", Schemas.Sourcing);
            invitations.WithOwner().HasForeignKey("RfqId");
            invitations.HasKey(i => i.Id);
            invitations.Property(i => i.Id).ValueGeneratedNever();
            invitations.HasIndex("RfqId", nameof(RfqInvitation.SupplierId)).IsUnique();
            invitations.HasIndex(i => i.SupplierId);
        });
        builder.Navigation(r => r.Invitations).HasField("_invitations").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(r => r.Awards, awards =>
        {
            awards.ToTable("RfqAwards", Schemas.Sourcing);
            awards.WithOwner().HasForeignKey("RfqId");
            awards.HasKey(a => a.Id);
            awards.Property(a => a.Id).ValueGeneratedNever();
            awards.Property(a => a.UnitPrice).HasPrecision(19, 4);
            awards.Property(a => a.VatCategory).HasConversion<string>().HasMaxLength(20);
            awards.Property(a => a.Justification).HasMaxLength(Rfq.ReasonMaxLength);
        });
        builder.Navigation(r => r.Awards).HasField("_awards").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class QuotationConfiguration : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> builder)
    {
        builder.ToTable("Quotations", Schemas.Sourcing);
        builder.Property(q => q.Id).ValueGeneratedNever();
        builder.Property(q => q.Notes).HasMaxLength(Quotation.NotesMaxLength);

        // One quotation per supplier and RFQ, enforced by the database (two concurrent first submissions cannot both win).
        builder.HasIndex(q => new { q.RfqId, q.SupplierId }).IsUnique();
        builder.HasOne<Rfq>().WithMany().HasForeignKey(q => q.RfqId).OnDelete(DeleteBehavior.Restrict);
        builder.PrimitiveCollection(q => q.AttachmentFileIds).IsRequired();

        builder.OwnsMany(q => q.Lines, lines =>
        {
            lines.ToTable("QuotationLines", Schemas.Sourcing);
            lines.WithOwner().HasForeignKey("QuotationId");
            lines.HasKey(l => l.Id);
            lines.Property(l => l.Id).ValueGeneratedNever();
            lines.Property(l => l.UnitPrice).HasPrecision(19, 4);
            lines.Property(l => l.VatCategory).HasConversion<string>().HasMaxLength(20);
        });
        builder.Navigation(q => q.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
