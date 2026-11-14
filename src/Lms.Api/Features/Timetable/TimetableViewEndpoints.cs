using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.People;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Lms.Infrastructure.Timetable;
using Lms.Shared;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Timetable;

/// <summary>
/// The own-data timetable views (FR-TT-04, M5 Task 15): <c>GET /api/me/timetable</c> for a teacher (own slots across classes plus the lessons they substitute on, on the
/// exception date only) and a student (the own class), and <c>GET /api/students/{id}/timetable</c> for a guardian (own children only) or the student. Published slots only;
/// a student who is not the caller's own child or self is a 404, exactly like a student that does not exist. At most 31 days.
/// </summary>
public static class TimetableViewEndpoints
{
    public static IEndpointRouteBuilder MapTimetableViews(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.MeTimetable, MeAsync).RequireAuthorization().RequirePermission(Permissions.Timetable.ViewOwn);
        app.MapGet(Routes.StudentTimetablePattern, StudentAsync).RequireAuthorization().RequirePermission(Permissions.Timetable.ViewOwn);
        return app;
    }

    private static async Task<IResult> MeAsync(
        DateOnly? from, DateOnly? to, LmsDbContext db, ICurrentContext current, TimetableViewService views, SubstituteRelationship substitutes, CancellationToken ct)
    {
        if (current.UserId is not { } me || current.OrganizationId is not { } organizationId)
        {
            return ApiProblems.NotFound();
        }

        if (RangeProblem(from, to) is { } bad)
        {
            return bad;
        }

        var published = db.Set<TimetableSlot>().Where(s => s.State == TimetableSlot.PublishedState);
        // The lessons one covers can be in another class or school of the client: read across the client, for the caller's own substitutions only.
        var substitutions = await substitutes.SubstitutionsAsync(organizationId, me, from!.Value, to!.Value, ct);
        var substituteSlotIds = substitutions.Select(x => x.SlotId).Distinct().ToList();
        var own = await published.Where(s => s.TeacherUserId == me).ToListAsync(ct);
        var covered = await views.SubstitutedSlotsAsync(organizationId, substituteSlotIds, ct);
        var teacherSlots = own.Concat(covered).DistinctBy(s => s.Id).ToList();
        // A lesson of someone else appears only on the date one substitutes on (L15): never on the other weeks of the slot.
        var onlyDates = teacherSlots.Where(s => s.TeacherUserId != me).ToDictionary(
            s => s.Id, s => (IReadOnlySet<DateOnly>)substitutions.Where(x => x.SlotId == s.Id).Select(x => x.Date).ToHashSet());

        var ownProfile = await db.Set<StudentProfile>().Where(p => p.UserId == me).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        var studentSlots = ownProfile is { } profile ? await ClassSlotsAsync(db, profile, from!.Value, ct) : [];
        var slots = teacherSlots.Concat(studentSlots).DistinctBy(s => s.Id).ToList();
        return Results.Ok(await views.BuildAsync(slots, from!.Value, to!.Value, organizationId, onlyDates, ct));
    }

    private static async Task<IResult> StudentAsync(
        Guid id, DateOnly? from, DateOnly? to, LmsDbContext db, ICurrentContext current, TimetableViewService views, CancellationToken ct)
    {
        if (current.UserId is not { } me || current.OrganizationId is not { } organizationId)
        {
            return ApiProblems.NotFound();
        }

        if (RangeProblem(from, to) is { } bad)
        {
            return bad;
        }

        // Own children only: an active link of the caller's guardian profile, or the student themself. Everything else is as if it did not exist.
        var guardianId = await db.Set<GuardianProfile>().Where(g => g.UserId == me).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(ct);
        var isChild = guardianId is { } g && await db.Set<StudentGuardian>().AnyAsync(l => l.GuardianId == g && l.StudentId == id, ct);
        var isSelf = await db.Set<StudentProfile>().AnyAsync(p => p.Id == id && p.UserId == me, ct);
        if (!isChild && !isSelf)
        {
            return ApiProblems.NotFound();
        }

        var slots = await ClassSlotsAsync(db, id, from!.Value, ct);
        return Results.Ok(await views.BuildAsync(slots, from.Value, to!.Value, organizationId, null, ct));
    }

    /// <summary>The published slots of the class the student is placed in on the first date of the range (a transfer inside the range shows the class of the first day for the whole range).</summary>
    private static async Task<List<TimetableSlot>> ClassSlotsAsync(LmsDbContext db, Guid studentId, DateOnly on, CancellationToken ct)
    {
        var classIds = db.Set<ClassStudent>()
            .Where(c => c.StudentId == studentId && c.Status == ClassStudent.ActiveStatus && c.From <= on && (c.To == null || c.To >= on)).Select(c => c.ClassId);
        return await db.Set<TimetableSlot>().Where(s => s.State == TimetableSlot.PublishedState && classIds.Contains(s.ClassId)).ToListAsync(ct);
    }

    private static IResult? RangeProblem(DateOnly? from, DateOnly? to) =>
        from is null || to is null || to < from || to.Value.DayNumber - from.Value.DayNumber + 1 > TimetableViewService.MaxDays
            ? ApiProblems.BadRequest(ProblemCodes.validation_failed, "from and to are required, in order, at most 31 days.")
            : null;
}
