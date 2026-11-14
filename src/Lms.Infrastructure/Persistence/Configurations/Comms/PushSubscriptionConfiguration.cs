using Lms.Domain.Comms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Comms;

internal sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("PushSubscriptions");
        builder.Property(x => x.Endpoint).HasMaxLength(PushSubscription.MaxEndpointLength).IsRequired();
        builder.Property(x => x.EndpointHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProtectedP256dh).HasMaxLength(600).IsRequired();
        builder.Property(x => x.ProtectedAuth).HasMaxLength(600).IsRequired();
        builder.Property(x => x.DeviceLabel).HasMaxLength(PushSubscription.MaxLabelLength).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.EndpointHash }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.UserId });
    }
}
