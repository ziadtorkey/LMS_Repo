using Lms.Domain.Comms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Comms;

internal sealed class ContactSuppressionConfiguration : IEntityTypeConfiguration<ContactSuppression>
{
    public void Configure(EntityTypeBuilder<ContactSuppression> builder)
    {
        builder.ToTable("ContactSuppressions");
        builder.Property(x => x.Channel).HasMaxLength(20).IsRequired();
        builder.Property(x => x.AddressHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(30).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.Channel, x.AddressHash }).IsUnique();
    }
}
