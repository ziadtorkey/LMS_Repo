using Lms.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Access;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.Property(x => x.BuiltInKey).HasMaxLength(50);
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.BaseKind).HasConversion<string>().HasMaxLength(20);
        // Unique per client; custom roles have a NULL key and MySQL allows any number of NULLs in a unique index.
        builder.HasIndex(x => new { x.OrganizationId, x.BuiltInKey }).IsUnique();
    }
}