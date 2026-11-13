using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Forums;
using Lms.Infrastructure.Persistence.Configurations.Academic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Forums;

internal sealed class ForumThreadConfiguration : IEntityTypeConfiguration<ForumThread>
{
    public void Configure(EntityTypeBuilder<ForumThread> builder)
    {
        builder.ToTable("ForumThreads");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.LastActivityAtUtc).HasColumnType("datetime(6)");
        builder.HasIndex(x => new { x.ClassCourseId, x.IsPinned, x.LastActivityAtUtc });
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ForumPostConfiguration : IEntityTypeConfiguration<ForumPost>
{
    public void Configure(EntityTypeBuilder<ForumPost> builder)
    {
        builder.ToTable("ForumPosts");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.BodyHtml).HasColumnType("longtext").IsRequired();
        builder.Property(x => x.EditedAtUtc).HasColumnType("datetime(6)");
        builder.HasIndex(x => new { x.ThreadId, x.CreatedAt });
        builder.HasOne<ForumThread>().WithMany().HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ForumPostAttachmentConfiguration : IEntityTypeConfiguration<ForumPostAttachment>
{
    public void Configure(EntityTypeBuilder<ForumPostAttachment> builder)
    {
        builder.ToTable("ForumPostAttachments");
        AcademicColumns.Unit(builder);
        builder.HasIndex(x => x.PostId);
        builder.HasIndex(x => x.AssetId);
        builder.HasOne<ForumPost>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CourseAnnouncementConfiguration : IEntityTypeConfiguration<CourseAnnouncement>
{
    public void Configure(EntityTypeBuilder<CourseAnnouncement> builder)
    {
        builder.ToTable("CourseAnnouncements");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.BodyHtml).HasColumnType("longtext").IsRequired();
        builder.HasIndex(x => new { x.ClassCourseId, x.IsPinned, x.CreatedAt });
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
    }
}
