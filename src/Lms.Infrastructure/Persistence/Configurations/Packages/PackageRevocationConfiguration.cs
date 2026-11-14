using Lms.Domain.Packages;
using Lms.Infrastructure.Persistence.Configurations.Academic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Packages;

internal sealed class PackageRevocationConfiguration : IEntityTypeConfiguration<PackageRevocation>
{
    public void Configure(EntityTypeBuilder<PackageRevocation> builder)
    {
        builder.ToTable("PackageRevocations");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.RevokedAtUtc).IsRequired();
        builder.HasOne<PackageUpload>().WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.PackageId });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}
