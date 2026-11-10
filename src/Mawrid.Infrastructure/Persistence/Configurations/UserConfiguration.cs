using Mawrid.Domain.Identity;
using Mawrid.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mawrid.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", Schemas.Identity);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Email).HasMaxLength(User.EmailMaxLength).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(User.DisplayNameMaxLength).IsRequired();

        // The email is the login name, so it is unique across all organizations.
        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.OrganizationId);
        builder.HasOne<Organization>().WithMany().HasForeignKey(u => u.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        // Same id as the Identity credential row (one-to-one).
        builder.HasOne<UserCredential>().WithOne().HasForeignKey<UserCredential>(c => c.Id).OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(u => u.IsSupplierUser);
        builder.OwnsMany(u => u.Roles, roles =>
        {
            roles.ToTable("UserRoleAssignments", Schemas.Identity);
            roles.WithOwner().HasForeignKey("UserId");
            roles.Property<int>("Id");
            roles.HasKey("Id");
            roles.Property(r => r.Role).HasConversion<string>().HasMaxLength(30);
            roles.Ignore(r => r.IsScoped);
        });
        builder.Navigation(u => u.Roles).HasField("_roles").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
