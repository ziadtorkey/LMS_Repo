using Lms.Domain.Grading;
using Lms.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Academic;

internal sealed class GradeSnapshotConfiguration : IEntityTypeConfiguration<GradeSnapshot>
{
    public void Configure(EntityTypeBuilder<GradeSnapshot> builder)
    {
        builder.ToTable("GradeSnapshots");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ValuesJson).HasColumnType("json").IsRequired();
        builder.Property(x => x.AtUtc).HasPrecision(6);
        builder.HasOne<Gradebook>().WithMany().HasForeignKey(x => x.GradebookId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.GradebookId, x.StudentProfileId, x.Kind });
        builder.HasIndex(x => x.BatchId);
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}
