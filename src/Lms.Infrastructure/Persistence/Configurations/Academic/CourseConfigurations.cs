using Lms.Domain.Common;
using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Academic;

internal static class AcademicColumns
{
    public const string AsciiPath = "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci";

    /// <summary>Common mapping of a unit-owned academic row: ascii path column and the restrict FK to the unit.</summary>
    public static void Unit<T>(EntityTypeBuilder<T> builder) where T : UnitOwnedEntity
    {
        builder.Property(x => x.OrgUnitPath).HasColumnType(AsciiPath).IsRequired();
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable("Courses");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CodeKey).HasMaxLength(50);
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DescriptionAr).HasMaxLength(2000);
        builder.Property(x => x.DescriptionEn).HasMaxLength(2000);
        builder.Property(x => x.Language).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.CodeKey }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.OrgUnitId, x.Status });
    }
}

internal sealed class CourseAllocationConfiguration : IEntityTypeConfiguration<CourseAllocation>
{
    public void Configure(EntityTypeBuilder<CourseAllocation> builder)
    {
        builder.ToTable("CourseAllocations");
        AcademicColumns.Unit(builder);
        builder.HasIndex(x => new { x.OrgUnitId, x.CourseKey }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CourseRestrictionConfiguration : IEntityTypeConfiguration<CourseRestriction>
{
    public void Configure(EntityTypeBuilder<CourseRestriction> builder)
    {
        builder.ToTable("CourseRestrictions");
        AcademicColumns.Unit(builder);
        builder.Ignore(x => x.Values);
        builder.Property(x => x.Type).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ValuesJson).HasColumnType("json").IsRequired();
        builder.HasIndex(x => new { x.CourseId, x.Type });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CourseSupervisorConfiguration : IEntityTypeConfiguration<CourseSupervisor>
{
    public void Configure(EntityTypeBuilder<CourseSupervisor> builder)
    {
        builder.ToTable("CourseSupervisors");
        AcademicColumns.Unit(builder);
        builder.HasIndex(x => new { x.CourseId, x.UserId, x.LiveKey }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.UserId });
        builder.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lms.Domain.Identity.User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
