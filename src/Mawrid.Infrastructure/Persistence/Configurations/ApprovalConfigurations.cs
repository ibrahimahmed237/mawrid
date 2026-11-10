using System.Text.Json;
using Mawrid.Domain.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mawrid.Infrastructure.Persistence.Configurations;

internal sealed class ApprovalPolicyConfiguration : IEntityTypeConfiguration<ApprovalPolicy>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public void Configure(EntityTypeBuilder<ApprovalPolicy> builder)
    {
        builder.ToTable("ApprovalPolicies", Schemas.Approvals);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.DocumentType).HasMaxLength(50).IsRequired();
        builder.HasIndex(p => new { p.OrganizationId, p.DocumentType }).IsUnique();

        // The whole matrix is small and always read together, so it is one JSON column.
        builder.Ignore(p => p.Rules);
        builder.Property<List<ApprovalRule>>("_rules")
            .HasColumnName("Rules")
            .HasConversion(
                rules => JsonSerializer.Serialize(rules, Json),
                json => JsonSerializer.Deserialize<List<ApprovalRule>>(json, Json) ?? new List<ApprovalRule>(),
                new ValueComparer<List<ApprovalRule>>(
                    (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
                    rules => JsonSerializer.Serialize(rules, Json).GetHashCode(StringComparison.Ordinal),
                    rules => JsonSerializer.Deserialize<List<ApprovalRule>>(JsonSerializer.Serialize(rules, Json), Json)!))
            .IsRequired();
    }
}

internal sealed class ApproverGroupConfiguration : IEntityTypeConfiguration<ApproverGroup>
{
    public void Configure(EntityTypeBuilder<ApproverGroup> builder)
    {
        builder.ToTable("ApproverGroups", Schemas.Approvals);
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.Name).HasMaxLength(ApproverGroup.NameMaxLength).IsRequired();
        builder.HasIndex(g => new { g.OrganizationId, g.Name }).IsUnique();

        // A primitive collection: EF Core stores the list of ids as a JSON array.
        builder.PrimitiveCollection(g => g.MemberUserIds).IsRequired();
    }
}

internal sealed class ApprovalDelegationConfiguration : IEntityTypeConfiguration<ApprovalDelegation>
{
    public void Configure(EntityTypeBuilder<ApprovalDelegation> builder)
    {
        builder.ToTable("ApprovalDelegations", Schemas.Approvals);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Reason).HasMaxLength(200);
        builder.HasIndex(d => new { d.ToUserId, d.ValidFrom, d.ValidTo });
    }
}

internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("ApprovalRequests", Schemas.Approvals);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.DocumentType).HasMaxLength(50).IsRequired();
        builder.Property(r => r.DocumentTitle).HasMaxLength(300).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(r => new { r.DocumentType, r.DocumentId });
        builder.HasIndex(r => new { r.OrganizationId, r.Status });
        builder.Ignore(r => r.CurrentStep);

        builder.OwnsMany(r => r.Steps, steps =>
        {
            steps.ToTable("ApprovalSteps", Schemas.Approvals);
            steps.WithOwner().HasForeignKey("ApprovalRequestId");
            steps.HasKey("ApprovalRequestId", nameof(ApprovalStep.Order));
            steps.Property(s => s.Order).ValueGeneratedNever();
            steps.Property(s => s.Name).HasMaxLength(100).IsRequired();
            steps.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            steps.Property(s => s.Comment).HasMaxLength(ApprovalRequest.CommentMaxLength);
            steps.PrimitiveCollection(s => s.EligibleApproverIds).IsRequired();
        });
        builder.Navigation(r => r.Steps).HasField("_steps").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
