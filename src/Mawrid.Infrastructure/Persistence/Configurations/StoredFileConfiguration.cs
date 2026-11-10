using Mawrid.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mawrid.Infrastructure.Persistence.Configurations;

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("StoredFiles", Schemas.Files);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.FileName).HasMaxLength(StoredFile.FileNameMaxLength).IsRequired();
        builder.Property(f => f.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(f => f.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(f => f.StorageKey).HasMaxLength(200).IsRequired();
        builder.Property(f => f.ScanStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.ScanResult).HasMaxLength(200);
        builder.HasIndex(f => new { f.OrganizationId, f.Sha256 });
    }
}
