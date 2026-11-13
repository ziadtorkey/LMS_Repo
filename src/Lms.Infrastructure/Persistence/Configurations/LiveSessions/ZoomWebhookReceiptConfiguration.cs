using Lms.Domain.LiveSessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.LiveSessions;

internal sealed class ZoomWebhookReceiptConfiguration : IEntityTypeConfiguration<ZoomWebhookReceipt>
{
    public void Configure(EntityTypeBuilder<ZoomWebhookReceipt> builder)
    {
        builder.ToTable("ZoomWebhookReceipts");
        builder.Property(x => x.EventId).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.EventId).IsUnique();
        builder.HasIndex(x => x.ReceivedAtUtc);
    }
}
