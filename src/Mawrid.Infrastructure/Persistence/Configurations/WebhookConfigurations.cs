using Mawrid.Domain.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mawrid.Infrastructure.Persistence.Configurations;

internal sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        builder.ToTable("WebhookSubscriptions", Schemas.Integrations);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Name).HasMaxLength(WebhookSubscription.NameMaxLength).IsRequired();
        builder.Property(s => s.Url).HasMaxLength(WebhookSubscription.UrlMaxLength).IsRequired();
        builder.Property(s => s.ProtectedSecret).HasMaxLength(2000).IsRequired();
        builder.PrimitiveCollection(s => s.EventTypes).IsRequired();
    }
}

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("WebhookDeliveries", Schemas.Integrations);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.EventType).HasMaxLength(50).IsRequired();
        builder.Property(d => d.Payload).IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.LastError).HasMaxLength(WebhookDelivery.ErrorMaxLength);
        builder.HasIndex(d => new { d.Status, d.NextAttemptAtUtc });
        builder.HasIndex(d => new { d.OrganizationId, d.CreatedAtUtc });
        builder.HasOne<WebhookSubscription>().WithMany().HasForeignKey(d => d.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
    }
}
