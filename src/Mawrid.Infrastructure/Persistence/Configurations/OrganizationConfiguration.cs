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
    }
}
