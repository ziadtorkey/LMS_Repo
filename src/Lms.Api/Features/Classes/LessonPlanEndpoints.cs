using Lms.Api.Features.Content;
using Lms.Api.Infrastructure;
using Lms.Domain.Content;
using Lms.Shared.Content;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Infrastructure.Classes.LessonPlans;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Classes;

/// <summary>
/// The plan tree of a class course (FR-LP-05, read only: the plan is a locked copy of the blueprint, D7; authoring of modules
/// is M4). Nothing here writes sections or lessons.
/// </summary>
public static class LessonPlanEndpoints
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet(Routes.ClassCourseLessonPlanPattern, GetAsync).RequireAuthorization().RequirePermission(Permissions.LessonPlans.View);

    private static async Task<IResult> GetAsync(
        Guid id, HttpContext http, LmsDbContext db, LessonPlanReads reads, ISettingsResolver settings, ModuleAccess access,
        ProgressReader progress, IClock clock, CancellationToken ct)
    {
        // The permission at the class course's unit, then the relationship: a teacher of another class course gets 404, a
        // student only a class course they are enrolled in.
        var (classCourse, problem) = await http.CheckClassCourseAsync(Permissions.LessonPlans.View, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var label = (await settings.ResolveAsync(SettingKeys.BlueprintsSectionLabel, classCourse!.OrgUnitPath, ct)).Value;
        var plan = await db.Set<ClassLessonPlan>().FirstOrDefaultAsync(p => p.ClassCourseId == id, ct);
        if (plan is null)
        {
            return Results.Ok(new ClassLessonPlanDto(id, null, null, label, []));
        }

        var cls = await db.Set<Class>().FirstOrDefaultAsync(c => c.Id == classCourse.ClassId, ct);
        var year = cls is null ? null : await reads.YearAsync(cls, ct);
        var hideRemoved = await access.IsStudentAsync(ct);
        var now = clock.UtcNow;
        var (modulesByLesson, snapshot) = await ModulesAsync(db, access, progress, id, hideRemoved, now, ct);

        var sections = await db.Set<ClassSection>().Where(s => s.PlanId == plan.Id).OrderBy(s => s.Order).ToListAsync(ct);
        var lessons = await db.Set<ClassLesson>().Where(l => l.PlanId == plan.Id).OrderBy(l => l.Order).ToListAsync(ct);
        DateOnly? Planned(int? offset) => year is not null && offset is { } days ? year.StartDate.AddDays(days) : null;

        var tree = sections
            .Where(s => !hideRemoved || !s.IsHidden)
            .Select(s => new ClassSectionDto(
                s.Key, s.Order, s.TitleAr, s.TitleEn, s.IsHidden,
                lessons.Where(l => l.SectionId == s.Id && (!hideRemoved || !l.IsHidden))
                    .Select(l => new ClassLessonDto(
                        l.Key, l.Order, l.TitleAr, l.TitleEn, l.ObjectivesAr, l.ObjectivesEn, Planned(l.PlannedOffsetDays), l.IsHidden,
                        modulesByLesson.GetValueOrDefault(l.Key, []),
                        snapshot is null ? null : snapshot.Lessons.GetValueOrDefault(l.Key, new ProgressDto(0, 0))))
                    .ToList()))
            .ToList();
        return Results.Ok(new ClassLessonPlanDto(id, plan.BlueprintVersion, plan.SyncedAt, label, tree, snapshot?.Course, now));
    }

    /// <summary>
    /// The modules under each lesson key: a student gets only what <see cref="ModuleAccess"/> lets them see, with their completion
    /// state and progress; staff get every module with <c>IsVisibleNow</c> and no progress (M4a Task 3 and 6).
    /// </summary>
    private static async Task<(Dictionary<Guid, List<ModuleNodeDto>> ByLesson, ProgressReader.Snapshot? Snapshot)> ModulesAsync(
        LmsDbContext db, ModuleAccess access, ProgressReader progress, Guid classCourseId, bool isStudent, DateTime now, CancellationToken ct)
    {
        List<LessonModule> modules;
        ProgressReader.Snapshot? snapshot = null;
        if (isStudent)
        {
            if (await access.StudentIdAsync(ct) is not { } studentId)
            {
                return ([], new ProgressReader.Snapshot(new ProgressDto(0, 0), new Dictionary<Guid, ProgressDto>(), [], new Dictionary<Guid, string>(), null));
            }

            var group = await access.GroupOfAsync(classCourseId, studentId, ct);
            modules = (await access.VisibleTo(classCourseId, group, now).ToListAsync(ct)).Select(p => p.Module).ToList();
            snapshot = await progress.ReadAsync(classCourseId, studentId, ct);
        }
        else
        {
            modules = await db.Set<LessonModule>().Where(m => m.ClassCourseId == classCourseId).OrderBy(m => m.Order).ThenBy(m => m.Id).ToListAsync(ct);
        }

        var groups = await ModuleMapper.GroupsAsync(db, modules, ct);
        var byLesson = modules.GroupBy(m => m.LessonKey).ToDictionary(
            g => g.Key, g => g.Select(m => ModuleMapper.Node(m, now, groups, snapshot?.States)).ToList());
        return (byLesson, snapshot);
    }
}
