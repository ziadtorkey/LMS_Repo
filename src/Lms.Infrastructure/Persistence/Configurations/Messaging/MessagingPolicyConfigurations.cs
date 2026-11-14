using Lms.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Messaging;

internal sealed class MessagingRuleConfiguration : IEntityTypeConfiguration<MessagingRule>
{
    public void Configure(EntityTypeBuilder<MessagingRule> builder)
    {
        builder.ToTable("MessagingRules");
        builder.Property(x => x.FromRole).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ToRole).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Mode).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.FromRole, x.ToRole }).IsUnique();
    }
}

internal sealed class UserBlockConfiguration : IEntityTypeConfiguration<UserBlock>
{
    public void Configure(EntityTypeBuilder<UserBlock> builder)
    {
        builder.ToTable("UserBlocks");
        builder.Property(x => x.BlockKey).HasMaxLength(70);
        builder.HasIndex(x => x.BlockKey).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.BlockerUserId });
        builder.HasIndex(x => new { x.OrganizationId, x.BlockedUserId });
    }
}

internal sealed class MessagingRestrictionConfiguration : IEntityTypeConfiguration<MessagingRestriction>
{
    public void Configure(EntityTypeBuilder<MessagingRestriction> builder)
    {
        builder.ToTable("MessagingRestrictions");
        builder.HasIndex(x => new { x.OrganizationId, x.UserId });
    }
}
