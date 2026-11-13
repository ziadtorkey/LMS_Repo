using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.People;
using Lms.Domain.Quizzes;
using Lms.Infrastructure.Persistence.Configurations.Academic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Content;

internal sealed class QuizConfiguration : IEntityTypeConfiguration<Quiz>
{
    public void Configure(EntityTypeBuilder<Quiz> builder)
    {
        builder.ToTable("Quizzes");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.DescriptionAr).HasMaxLength(2000);
        builder.Property(x => x.DescriptionEn).HasMaxLength(2000);
        builder.Property(x => x.GradingMethod).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ReviewPolicy).HasMaxLength(20).IsRequired();
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LessonModule>().WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ModuleKey).IsUnique();
        builder.HasIndex(x => new { x.ClassCourseId, x.LessonKey });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}

internal sealed class QuizQuestionConfiguration : IEntityTypeConfiguration<QuizQuestion>
{
    public void Configure(EntityTypeBuilder<QuizQuestion> builder)
    {
        builder.ToTable("QuizQuestions");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Type).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Text).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Points).HasPrecision(7, 2);
        builder.HasOne<Quiz>().WithMany().HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.QuizId, x.Order });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}

internal sealed class QuizOptionConfiguration : IEntityTypeConfiguration<QuizOption>
{
    public void Configure(EntityTypeBuilder<QuizOption> builder)
    {
        builder.ToTable("QuizOptions");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Text).HasMaxLength(1000).IsRequired();
        builder.HasOne<QuizQuestion>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.QuestionId, x.Order });
        builder.HasIndex(x => x.QuizId);
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}

internal sealed class QuizAttemptConfiguration : IEntityTypeConfiguration<QuizAttempt>
{
    public void Configure(EntityTypeBuilder<QuizAttempt> builder)
    {
        builder.ToTable("QuizAttempts");
        ScopedChild.Map(builder, x => x.OrgUnitPath, x => x.OrgUnitId);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Score).HasPrecision(9, 2);
        builder.Property(x => x.MaxScore).HasPrecision(9, 2);
        builder.HasOne<Quiz>().WithMany().HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.QuizId, x.StudentId, x.Number }).IsUnique();
        builder.HasIndex(x => new { x.ClassCourseId, x.StudentId });
        builder.HasIndex(x => new { x.Status, x.EndsAtUtc });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}

internal sealed class QuizAnswerConfiguration : IEntityTypeConfiguration<QuizAnswer>
{
    public void Configure(EntityTypeBuilder<QuizAnswer> builder)
    {
        builder.ToTable("QuizAnswers");
        ScopedChild.Map(builder, x => x.OrgUnitPath, x => x.OrgUnitId);
        builder.Property(x => x.OptionOrder).HasMaxLength(500).IsRequired();
        builder.Property(x => x.SelectedOptionIds).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Text).HasColumnType("longtext");
        builder.Property(x => x.Feedback).HasMaxLength(2000);
        builder.Property(x => x.AwardedPoints).HasPrecision(7, 2);
        builder.HasOne<QuizAttempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QuizQuestion>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.AttemptId, x.QuestionId }).IsUnique();
        builder.HasIndex(x => x.QuizId);
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}
