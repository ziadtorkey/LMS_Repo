using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Content;

/// <summary><see cref="IModuleWriter"/> on the scoped context (M4a Task 3).</summary>
public sealed class ModuleWriter(LmsDbContext db, PlacementGuard guard, IClock clock) : IModuleWriter
{
    public async Task<Guid> CreateForRefAsync(
        Guid classCourseId, Guid lessonKey, string kind, Guid refId, ModuleMetadata metadata, CancellationToken ct = default)
    {
        if (!ModuleKinds.All.Contains(kind) || ModuleKinds.Content.Contains(kind))
        {
            throw new ModuleRuleException(ProblemCodeValidationFailed, $"Kind {kind} is not created for a reference.");
        }

        var classCourse = await db.Set<ClassCourse>().FirstOrDefaultAsync(c => c.Id == classCourseId, ct)
                          ?? throw new ModuleRuleException(ProblemCodeValidationFailed, "Unknown class course.");
        var lesson = await (from p in db.Set<ClassLessonPlan>()
                            join l in db.Set<ClassLesson>() on p.Id equals l.PlanId
                            where p.ClassCourseId == classCourseId && l.Key == lessonKey
                            select l).FirstOrDefaultAsync(ct)
                     ?? throw new ModuleRuleException(ProblemCodeValidationFailed, "Unknown lesson.");
        await EnsureWritableAsync(classCourse, ct);
        await CheckAsync(classCourse, metadata, ct);

        var last = await db.Set<LessonModule>().Where(m => m.ClassLessonId == lesson.Id).MaxAsync(m => (int?)m.Order, ct) ?? 0;
        var module = new LessonModule(classCourse, lesson, kind, last + 1);
        Apply(module, metadata);
        module.SetRef(refId);
        db.Add(module);
        await db.SaveChangesAsync(ct);
        return module.Id;
    }

    public async Task UpdateMetadataAsync(Guid moduleId, ModuleMetadata metadata, CancellationToken ct = default)
    {
        var module = await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Id == moduleId, ct)
                     ?? throw new ModuleRuleException(ProblemCodeValidationFailed, "Unknown module.");
        var classCourse = await db.Set<ClassCourse>().FirstAsync(c => c.Id == module.ClassCourseId, ct);
        await EnsureWritableAsync(classCourse, ct);
        await CheckAsync(classCourse, metadata, ct);
        Apply(module, metadata);
        await Packages.PackageRevocationHooks.ModuleChangedAsync(db, module, clock.UtcNow, ct); // a hidden package module revokes its tokens
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteForRefAsync(string kind, Guid refId, CancellationToken ct = default)
    {
        var modules = await db.Set<LessonModule>().Where(m => m.Kind == kind && m.RefId == refId).ToListAsync(ct);
        db.RemoveRange(modules);
        await db.SaveChangesAsync(ct);
    }

    private const string ProblemCodeValidationFailed = "validation_failed";

    private static void Apply(LessonModule module, ModuleMetadata m) =>
        module.Apply(m.TitleAr, m.TitleEn, m.Visibility, m.VisibleFromUtc, m.VisibleToUtc, m.Target, m.GroupId, m.CompletionRule, m.CompletionMinScore);

    private async Task EnsureWritableAsync(ClassCourse classCourse, CancellationToken ct)
    {
        var cls = await db.Set<Class>().FirstAsync(c => c.Id == classCourse.ClassId, ct);
        await guard.EnsureWritableAsync(cls, ct);
    }

    private async Task CheckAsync(ClassCourse classCourse, ModuleMetadata m, CancellationToken ct)
    {
        if (!ModuleVisibilities.All.Contains(m.Visibility) || !ModuleTargets.All.Contains(m.Target) || !CompletionRules.All.Contains(m.CompletionRule))
        {
            throw new ModuleRuleException(ProblemCodeValidationFailed, "Unknown visibility, target or completion rule.");
        }

        var scheduled = m.Visibility == ModuleVisibilities.Scheduled;
        var rangeOk = scheduled
            ? (m.VisibleFromUtc is not null || m.VisibleToUtc is not null) && !(m.VisibleFromUtc is { } f && m.VisibleToUtc is { } t && f >= t)
            : m.VisibleFromUtc is null && m.VisibleToUtc is null;
        if (!rangeOk)
        {
            throw new ModuleRuleException("module_visibility_range_invalid", "The visibility range is invalid.");
        }

        var group = m.Target == ModuleTargets.Group;
        var targetOk = group == (m.GroupId is not null)
                       && (!group || await db.Set<StudentGroup>().AnyAsync(g => g.Id == m.GroupId && g.ClassCourseId == classCourse.Id, ct));
        if (!targetOk)
        {
            throw new ModuleRuleException("module_target_invalid", "The target group is not a group of this class course.");
        }
    }
}
