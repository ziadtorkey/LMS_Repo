using Lms.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Notifications;

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("NotificationPreferences");
        builder.Property(x => x.EventKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Channel).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.EventKey, x.Channel }).IsUnique();
    }
}
