using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Configuration;

internal sealed class OrgUnitPolicyConfiguration : IEntityTypeConfiguration<OrgUnitPolicy>
{
    public void Configure(EntityTypeBuilder<OrgUnitPolicy> builder)
    {
        builder.ToTable("OrgUnitPolicies");
        builder.Property(x => x.OrgUnitPath)
            .HasColumnType("varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci").IsRequired();
        builder.Property(x => x.PolicyKey).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.OrgUnitId, x.PolicyKey }).IsUnique();
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
