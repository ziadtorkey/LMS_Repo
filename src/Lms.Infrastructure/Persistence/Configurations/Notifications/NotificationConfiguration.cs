using Lms.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Notifications;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.Property(x => x.EventKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ParametersJson).HasColumnType("longtext").IsRequired();
        builder.Property(x => x.DeepLink).HasMaxLength(500);
        builder.HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt });
        builder.HasIndex(x => x.OrganizationId);
    }
}
