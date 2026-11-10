using Mawrid.Domain.Suppliers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mawrid.Infrastructure.Persistence.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers", Schemas.Suppliers);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.NameAr).HasMaxLength(Supplier.NameMaxLength).IsRequired();
        builder.Property(s => s.NameEn).HasMaxLength(Supplier.NameMaxLength).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.StatusReason).HasMaxLength(Supplier.ReasonMaxLength);

        // Value objects are stored as plain columns.
        builder.Property(s => s.VatNumber)
            .HasConversion(v => v.Value, v => VatNumber.Create(v).Value)
            .HasMaxLength(15)
            .IsRequired();
        builder.Property(s => s.CrNumber)
            .HasConversion(v => v.Value, v => CrNumber.Create(v).Value)
            .HasMaxLength(10)
            .IsRequired();

        builder.HasIndex(s => new { s.OrganizationId, s.VatNumber }).IsUnique();

        builder.ComplexProperty(s => s.Contact, contact =>
        {
            contact.Property(c => c.Name).HasColumnName("ContactName").HasMaxLength(200).IsRequired();
            contact.Property(c => c.Email).HasColumnName("ContactEmail").HasMaxLength(256).IsRequired();
            contact.Property(c => c.Phone).HasColumnName("ContactPhone").HasMaxLength(30);
        });

        builder.Ignore(s => s.ActiveBankAccount);
        builder.Ignore(s => s.HasPendingBankChange);
        builder.Ignore(s => s.CurrentDocuments);

        builder.OwnsMany(s => s.Documents, documents =>
        {
            documents.ToTable("SupplierDocuments", Schemas.Suppliers);
            documents.WithOwner().HasForeignKey("SupplierId");
            documents.HasKey(d => d.Id);
            documents.Property(d => d.Id).ValueGeneratedNever();
            documents.Property(d => d.Type).HasConversion<string>().HasMaxLength(30);
        });
        builder.Navigation(s => s.Documents).HasField("_documents").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(s => s.BankAccounts, accounts =>
        {
            accounts.ToTable("SupplierBankAccounts", Schemas.Suppliers);
            accounts.WithOwner().HasForeignKey("SupplierId");
            accounts.HasKey(a => a.Id);
            accounts.Property(a => a.Id).ValueGeneratedNever();
            accounts.Property(a => a.Iban)
                .HasConversion(v => v.Value, v => Iban.Create(v).Value)
                .HasMaxLength(24)
                .IsRequired();
            accounts.Property(a => a.BankName).HasMaxLength(Supplier.NameMaxLength).IsRequired();
            accounts.Property(a => a.AccountName).HasMaxLength(Supplier.NameMaxLength).IsRequired();
            accounts.Property(a => a.Status).HasConversion<string>().HasMaxLength(30);
        });
        builder.Navigation(s => s.BankAccounts).HasField("_bankAccounts").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
