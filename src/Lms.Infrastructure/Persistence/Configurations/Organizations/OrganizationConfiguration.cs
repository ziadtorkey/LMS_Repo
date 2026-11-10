using Lms.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Organizations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
    }
}