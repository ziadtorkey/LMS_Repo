using Lms.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Organizations;

internal sealed class LevelTypeConfiguration : IEntityTypeConfiguration<LevelType>
{
    public void Configure(EntityTypeBuilder<LevelType> builder)
    {
        builder.ToTable("LevelTypes");
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.OrganizationId);
    }
}