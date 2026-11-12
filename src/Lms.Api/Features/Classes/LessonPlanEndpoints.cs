using Lms.Api.Infrastructure;
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
        Guid id, HttpContext http, LmsDbContext db, LessonPlanReads reads, ISettingsResolver settings, ICurrentContext context,
        CancellationToken ct)
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
        var hideRemoved = await IsStudentAsync(db, context, ct);

        var sections = await db.Set<ClassSection>().Where(s => s.PlanId == plan.Id).OrderBy(s => s.Order).ToListAsync(ct);
        var lessons = await db.Set<ClassLesson>().Where(l => l.PlanId == plan.Id).OrderBy(l => l.Order).ToListAsync(ct);
        DateOnly? Planned(int? offset) => year is not null && offset is { } days ? year.StartDate.AddDays(days) : null;

        var tree = sections
            .Where(s => !hideRemoved || !s.IsHidden)
            .Select(s => new ClassSectionDto(
                s.Key, s.Order, s.TitleAr, s.TitleEn, s.IsHidden,
                lessons.Where(l => l.SectionId == s.Id && (!hideRemoved || !l.IsHidden))
                    .Select(l => new ClassLessonDto(
                        l.Key, l.Order, l.TitleAr, l.TitleEn, l.ObjectivesAr, l.ObjectivesEn, Planned(l.PlannedOffsetDays), l.IsHidden))
                    .ToList()))
            .ToList();
        return Results.Ok(new ClassLessonPlanDto(id, plan.BlueprintVersion, plan.SyncedAt, label, tree));
    }

    /// <summary>Students see the plan without the items that were removed from the blueprint (they stay for staff, as history).</summary>
    private static async Task<bool> IsStudentAsync(LmsDbContext db, ICurrentContext context, CancellationToken ct)
    {
        if (context.AssignmentId is not { } assignmentId)
        {
            return false;
        }

        var roleKey = await (
                from a in db.RoleAssignments
                join r in db.Roles on a.RoleId equals r.Id
                where a.Id == assignmentId
                select r.BuiltInKey)
            .FirstOrDefaultAsync(ct);
        return roleKey == BuiltInRoles.Student;
    }
}
