using Lms.Domain.Announcements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Announcements;

internal sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("Announcements");
        builder.Property(x => x.TitleAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TitleEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.BodyAr).HasColumnType("longtext");
        builder.Property(x => x.BodyEn).HasColumnType("longtext");
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.RoleFiltersCsv).HasMaxLength(60).IsRequired();
        builder.Ignore(x => x.RoleFilters);
        builder.Ignore(x => x.IsEditable);
        builder.HasIndex(x => new { x.OrganizationId, x.Status, x.PublishAtUtc });
        builder.HasIndex(x => new { x.OrganizationId, x.AuthorUserId, x.CreatedAt });
    }
}

internal sealed class AnnouncementTargetConfiguration : IEntityTypeConfiguration<AnnouncementTarget>
{
    public void Configure(EntityTypeBuilder<AnnouncementTarget> builder)
    {
        builder.ToTable("AnnouncementTargets");
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => x.AnnouncementId);
    }
}
