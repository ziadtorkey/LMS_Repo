using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.People;

internal static class PeopleColumns
{
    public const string AsciiPath = "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci";
}

internal sealed class StaffProfileConfiguration : IEntityTypeConfiguration<StaffProfile>
{
    public void Configure(EntityTypeBuilder<StaffProfile> builder)
    {
        builder.ToTable("StaffProfiles");
        builder.Property(x => x.OrgUnitPath).HasColumnType(PeopleColumns.AsciiPath).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameArKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEnKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Mobile).HasMaxLength(20);
        builder.Property(x => x.NationalId).HasMaxLength(20);
        builder.Property(x => x.JobTitleAr).HasMaxLength(150);
        builder.Property(x => x.JobTitleEn).HasMaxLength(150);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(x => new { x.OrganizationId, x.UserId });
        builder.HasIndex(x => new { x.OrganizationId, x.Email });
        builder.HasIndex(x => new { x.OrganizationId, x.Mobile });
        builder.HasIndex(x => new { x.OrganizationId, x.NationalId });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath, x.NameEnKey });
        builder.HasIndex(x => new { x.OrganizationId, x.Status, x.ExpiresOn });
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StudentProfileConfiguration : IEntityTypeConfiguration<StudentProfile>
{
    public void Configure(EntityTypeBuilder<StudentProfile> builder)
    {
        builder.ToTable("StudentProfiles");
        builder.Property(x => x.OrgUnitPath).HasColumnType(PeopleColumns.AsciiPath).IsRequired();
        builder.Property(x => x.StudentCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameArKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEnKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NicknameAr).HasMaxLength(100);
        builder.Property(x => x.NicknameEn).HasMaxLength(100);
        builder.Property(x => x.Gender).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Religion).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Nationality).HasMaxLength(30).IsRequired();
        builder.Property(x => x.NationalId).HasMaxLength(20);
        builder.Property(x => x.Mobile).HasMaxLength(20);
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.FeatureFlagOverridesJson).HasColumnType("json");
        // Unique over ALL rows, deleted ones included: a code is never reused inside a client, but another client may use it.
        builder.HasIndex(x => new { x.OrganizationId, x.StudentCode }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.NationalId });
        builder.HasIndex(x => new { x.OrganizationId, x.NameArKey, x.DateOfBirth });
        builder.HasIndex(x => new { x.OrganizationId, x.NameEnKey, x.DateOfBirth });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath, x.NameEnKey });
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GuardianProfileConfiguration : IEntityTypeConfiguration<GuardianProfile>
{
    public void Configure(EntityTypeBuilder<GuardianProfile> builder)
    {
        builder.ToTable("GuardianProfiles");
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameArKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEnKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Mobile).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.NationalId).HasMaxLength(20);
        builder.HasIndex(x => new { x.OrganizationId, x.NationalId });
        builder.HasIndex(x => new { x.OrganizationId, x.Mobile });
    }
}

internal sealed class StudentGuardianConfiguration : IEntityTypeConfiguration<StudentGuardian>
{
    public void Configure(EntityTypeBuilder<StudentGuardian> builder)
    {
        builder.ToTable("StudentGuardians");
        builder.Property(x => x.OrgUnitPath).HasColumnType(PeopleColumns.AsciiPath).IsRequired();
        builder.Property(x => x.Relation).HasMaxLength(30).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => x.StudentId);
        builder.HasIndex(x => x.GuardianId);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GuardianProfile>().WithMany().HasForeignKey(x => x.GuardianId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PersonStatusChangeConfiguration : IEntityTypeConfiguration<PersonStatusChange>
{
    public void Configure(EntityTypeBuilder<PersonStatusChange> builder)
    {
        builder.ToTable("PersonStatusChanges");
        builder.Property(x => x.OrgUnitPath).HasColumnType(PeopleColumns.AsciiPath).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.From).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.To).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.Kind, x.ProfileId, x.ChangedAt });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StudentChangeRequestConfiguration : IEntityTypeConfiguration<StudentChangeRequest>
{
    public void Configure(EntityTypeBuilder<StudentChangeRequest> builder)
    {
        builder.ToTable("StudentChangeRequests");
        builder.Property(x => x.OrgUnitPath).HasColumnType(PeopleColumns.AsciiPath).IsRequired();
        builder.Property(x => x.ChangesJson).HasColumnType("json").IsRequired();
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.DecisionComment).HasMaxLength(1000);
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath, x.Status });
        builder.HasIndex(x => x.StudentId);
        // One pending request per student, also under concurrency (PendingStudentId is null once decided).
        builder.HasIndex(x => x.PendingStudentId).IsUnique();
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
