using Lms.Domain.Comms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Comms;

internal sealed class DeliveryOutboxConfiguration : IEntityTypeConfiguration<DeliveryOutbox>
{
    public void Configure(EntityTypeBuilder<DeliveryOutbox> builder)
    {
        builder.ToTable("DeliveryOutbox");
        builder.Property(x => x.Channel).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TemplateKey).HasMaxLength(100);
        builder.Property(x => x.Provider).HasMaxLength(50);
        builder.Property(x => x.ProviderMessageId).HasMaxLength(200);
        builder.Property(x => x.LastErrorCode).HasMaxLength(100);

        // One delivery per (notification, channel); transactional rows (no notification) are not constrained (MySQL allows many NULLs).
        builder.HasIndex(x => new { x.NotificationId, x.Channel }).IsUnique();

        // The claim query: due pending rows of one client and channel, oldest first.
        builder.HasIndex(x => new { x.OrganizationId, x.Channel, x.Status, x.NextAttemptAtUtc });

        // Daily caps count a client's (or a user's) rows of one channel since the start of the client's day.
        builder.HasIndex(x => new { x.OrganizationId, x.UserId, x.Channel, x.SentAtUtc });
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}
