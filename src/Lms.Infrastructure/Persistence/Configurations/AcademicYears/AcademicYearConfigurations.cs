using Lms.Domain.AcademicYears;
using Lms.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Lms.Infrastructure.Persistence.Configurations.AcademicYears;

/// <summary>The MySQL provider hands back DATE columns as DateTime, so DateOnly is converted explicitly.</summary>
internal static class DateOnlyColumns
{
    private static readonly ValueConverter<DateOnly, DateTime> Converter =
        new(d => d.ToDateTime(TimeOnly.MinValue), d => DateOnly.FromDateTime(d));

    public static PropertyBuilder<DateOnly> AsDate(this PropertyBuilder<DateOnly> property) =>
        property.HasConversion(Converter).HasColumnType("date");
}

internal sealed class AcademicYearConfiguration : IEntityTypeConfiguration<AcademicYear>
{
    public void Configure(EntityTypeBuilder<AcademicYear> builder)
    {
        builder.ToTable("AcademicYears");
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.OrgUnitPath)
            .HasColumnType("varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci")
            .IsRequired();
        builder.Property(x => x.StartDate).AsDate();
        builder.Property(x => x.EndDate).AsDate();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.OrgUnitId, x.StartDate });
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TermConfiguration : IEntityTypeConfiguration<Term>
{
    public void Configure(EntityTypeBuilder<Term> builder)
    {
        builder.ToTable("Terms");
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.OrgUnitPath)
            .HasColumnType("varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci")
            .IsRequired();
        builder.Property(x => x.StartDate).AsDate();
        builder.Property(x => x.EndDate).AsDate();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.AcademicYearId, x.StartDate });
        builder.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        builder.ToTable("CalendarEvents");
        builder.Property(x => x.TitleAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TitleEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.OrgUnitPath)
            .HasColumnType("varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci")
            .IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.StartDate).AsDate();
        builder.Property(x => x.EndDate).AsDate();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.AcademicYearId, x.StartDate });
        builder.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
