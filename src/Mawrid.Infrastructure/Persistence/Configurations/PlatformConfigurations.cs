using Mawrid.Infrastructure.Auditing;
using Mawrid.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mawrid.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages", Schemas.Platform);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(300).IsRequired();
        builder.Property(m => m.Content).IsRequired();
        builder.Property(m => m.CorrelationId).HasMaxLength(64);
        builder.Property(m => m.LastError).HasMaxLength(2000);

        // The worker only looks at messages that are not done yet.
        builder.HasIndex(m => new { m.NextAttemptAtUtc, m.OccurredOnUtc })
            .HasFilter("[ProcessedOnUtc] IS NULL AND [DeadLetteredOnUtc] IS NULL");
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries", Schemas.Platform);
        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Changes).IsRequired();
        builder.Property(a => a.CorrelationId).HasMaxLength(64);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => new { a.OrganizationId, a.OccurredOnUtc });
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<Idempotency.IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<Idempotency.IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyKeys", Schemas.Platform);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Key).HasMaxLength(100).IsRequired();
        builder.Property(r => r.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(r => r.ResponseContentType).HasMaxLength(100);
        builder.Property(r => r.ResponseLocation).HasMaxLength(500);
        builder.HasIndex(r => new { r.OrganizationId, r.Key }).IsUnique();
        builder.HasIndex(r => r.ExpiresAtUtc);
    }
}
