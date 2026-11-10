using Mawrid.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mawrid.Infrastructure.Persistence.Configurations;

internal sealed class CostCenterConfiguration : IEntityTypeConfiguration<CostCenter>
{
    public void Configure(EntityTypeBuilder<CostCenter> builder)
    {
        builder.ToTable("CostCenters", Schemas.Identity);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Code).HasMaxLength(CostCenter.CodeMaxLength).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(CostCenter.NameMaxLength).IsRequired();

        builder.HasIndex(c => new { c.OrganizationId, c.Code }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
