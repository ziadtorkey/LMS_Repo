using Lms.Domain.Classes;
using Lms.Domain.Grading;
using Lms.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Academic;

internal sealed class GradeEventConfiguration : IEntityTypeConfiguration<GradeEvent>
{
    public void Configure(EntityTypeBuilder<GradeEvent> builder)
    {
        builder.ToTable("GradeEvents");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.SourceKind).HasConversion<int>();
        builder.Property(x => x.Points).HasPrecision(9, 2);
        builder.Property(x => x.MaxPoints).HasPrecision(9, 2);
        builder.Property(x => x.ScoredAtUtc).HasPrecision(6);
        builder.HasIndex(x => new { x.SourceKind, x.SourceId, x.StudentProfileId, x.Revision }).IsUnique();
        builder.HasIndex(x => new { x.ClassCourseId, x.StudentProfileId });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
    }
}
