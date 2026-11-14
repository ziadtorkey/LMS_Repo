using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Infrastructure.Timetable;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Timetable;

/// <summary>A class's weekly timetable for staff (FR-TT-02, FR-TT-03): read, build, validate and publish. Unit access decides; a miss is 404.</summary>
public static class TimetableEndpoints
{
    public static IEndpointRouteBuilder MapTimetable(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.ClassTimetablePattern, GetAsync).RequirePermission(Permissions.Timetable.View);
        app.MapPost(Routes.ClassTimetableSlotsPattern, CreateAsync).RequirePermission(Permissions.Timetable.Manage);
        app.MapPut(Routes.ClassTimetableSlotPattern, UpdateAsync).RequirePermission(Permissions.Timetable.Manage);
        app.MapDelete(Routes.ClassTimetableSlotPattern, DeleteAsync).RequirePermission(Permissions.Timetable.Manage);
        app.MapGet(Routes.ClassTimetableValidatePattern, ValidateAsync).RequirePermission(Permissions.Timetable.Manage);
        app.MapPost(Routes.ClassTimetablePublishPattern, PublishAsync).RequirePermission(Permissions.Timetable.Manage);
        return app;
    }

    private static async Task<IResult> GetAsync(
        Guid id, Guid? termId, HttpContext http, LmsDbContext db, TimetableService timetable, PeriodService periods, ISettingsResolver settings,
        IAccessEvaluator access, IClock clock, CancellationToken ct)
    {
        var (@class, refused) = await OpenAsync(http, timetable, id, Permissions.Timetable.View, ct);
        if (@class is null)
        {
            return refused!;
        }

        if (termId is null)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "termId is required.");
        }

        if (await timetable.FindTermAsync(@class, termId.Value, ct) is null)
        {
            return ApiProblems.NotFound();
        }

        var manage = (await access.EvaluateAsync(Permissions.Timetable.Manage, @class.OrgUnitPath, ct)).Allowed;
        var slots = await timetable.SlotsAsync(id, termId.Value, manage, ct);
        var unit = await db.OrgUnits.SingleAsync(u => u.Id == @class.OrgUnitId, ct);
        var (periodRows, inheritedFrom) = await periods.EffectiveAsync(unit, ct);
        var workingDays = (await settings.ResolveAsync(SettingKeys.TimetableWorkingDays, @class.OrgUnitPath, ct)).Value;
        var mode = (await settings.ResolveAsync(SettingKeys.TimetableConflictMode, @class.OrgUnitPath, ct)).Value;
        // Conflict badges are a builder aid: only for staff who can build, and read with one query for the whole term.
        var conflicting = manage ? await ConflictingIdsAsync(timetable, @class, termId.Value, slots, ct) : null;
        var dtos = await timetable.ToDtosAsync(@class.OrganizationId, slots, conflicting, ct);
        var draftCount = slots.Count(s => !s.IsPublished);
        return Results.Ok(new ClassTimetableDto(id, termId.Value, workingDays,
            periodRows.Select(p => new PeriodDto(p.Id, p.OrgUnitId, p.NameAr, p.NameEn, p.StartTime, p.EndTime, p.Order, inheritedFrom)).ToList(),
            dtos, mode, draftCount, clock.UtcNow));
    }

    private static async Task<IResult> CreateAsync(
        Guid id, SaveSlotRequest? body, HttpContext http, LmsDbContext db, TimetableService timetable, PeriodService periods, ISettingsResolver settings,
        CancellationToken ct)
    {
        var (@class, refused) = await OpenAsync(http, timetable, id, Permissions.Timetable.Manage, ct);
        if (@class is null)
        {
            return refused!;
        }

        if (await ValidateRequestAsync(@class, body, timetable, periods, db, ct) is { } invalid)
        {
            return invalid;
        }

        var slot = new TimetableSlot(@class, body!.ClassCourseId, body.TermId, body.TeacherUserId, body.DayOfWeek, body.PeriodId, Room(body.RoomName));
        return await SaveAsync(db, timetable, settings, @class, slot, isNew: true, ct);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, Guid sid, SaveSlotRequest? body, HttpContext http, LmsDbContext db, TimetableService timetable, PeriodService periods, ISettingsResolver settings,
        CancellationToken ct)
    {
        var (@class, refused) = await OpenAsync(http, timetable, id, Permissions.Timetable.Manage, ct);
        if (@class is null)
        {
            return refused!;
        }

        var slot = await db.Set<TimetableSlot>().SingleOrDefaultAsync(s => s.Id == sid && s.ClassId == id, ct);
        if (slot is null)
        {
            return ApiProblems.NotFound();
        }

        if (body?.Version is null)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "Version is required.");
        }

        if (body.TermId != slot.TermId)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "A slot cannot move to another term.");
        }

        if (await ValidateRequestAsync(@class, body, timetable, periods, db, ct) is { } invalid)
        {
            return invalid;
        }

        if (slot.Version != body.Version)
        {
            return await StaleAsync(timetable, slot, ct);
        }

        // Rule: editing a published slot returns it to Draft (the week is published again after the change); attendance sessions already generated from it are Task 16's to reconcile.
        slot.Move(body.ClassCourseId, body.TeacherUserId, body.DayOfWeek, body.PeriodId, Room(body.RoomName));
        return await SaveAsync(db, timetable, settings, @class, slot, isNew: false, ct);
    }

    private static async Task<IResult> DeleteAsync(Guid id, Guid sid, HttpContext http, LmsDbContext db, TimetableService timetable, CancellationToken ct)
    {
        var (@class, refused) = await OpenAsync(http, timetable, id, Permissions.Timetable.Manage, ct);
        if (@class is null)
        {
            return refused!;
        }

        var slot = await db.Set<TimetableSlot>().SingleOrDefaultAsync(s => s.Id == sid && s.ClassId == id, ct);
        if (slot is null)
        {
            return ApiProblems.NotFound();
        }

        db.Set<TimetableSlot>().Remove(slot);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ValidateAsync(Guid id, Guid? termId, HttpContext http, TimetableService timetable, CancellationToken ct)
    {
        var (@class, refused) = await OpenAsync(http, timetable, id, Permissions.Timetable.Manage, ct);
        if (@class is null)
        {
            return refused!;
        }

        if (termId is null || await timetable.FindTermAsync(@class, termId.Value, ct) is null)
        {
            return ApiProblems.NotFound();
        }

        var slots = await timetable.SlotsAsync(id, termId.Value, includeDrafts: true, ct);
        var (bySlot, others) = await timetable.ClashesForAsync(@class, termId.Value, slots, ct);
        var clashes = bySlot.Values.SelectMany(c => c).ToList();

        var described = await timetable.DescribeAsync(@class.OrganizationId, clashes, others, ct);
        return Results.Ok(new TimetableValidationDto(described.DistinctBy(c => (c.Kind, c.SlotId)).ToList()));
    }

    private static async Task<IResult> PublishAsync(
        Guid id, PublishTimetableRequest? body, HttpContext http, LmsDbContext db, TimetableService timetable, ISettingsResolver settings, IAttendanceGenerationQueue queue, CancellationToken ct)
    {
        var (@class, refused) = await OpenAsync(http, timetable, id, Permissions.Timetable.Manage, ct);
        if (@class is null)
        {
            return refused!;
        }

        if (body is null || await timetable.FindTermAsync(@class, body.TermId, ct) is null)
        {
            return ApiProblems.NotFound();
        }

        var drafts = await db.Set<TimetableSlot>().Where(s => s.ClassId == id && s.TermId == body.TermId && s.State == TimetableSlot.DraftState).ToListAsync(ct);

        // A clash created after the draft (the teacher got a slot elsewhere in between) must not go live in Block mode: nothing is published.
        var mode = (await settings.ResolveAsync(SettingKeys.TimetableConflictMode, @class.OrgUnitPath, ct)).Value;
        if (mode != ConflictModes.Warn && drafts.Count > 0)
        {
            var (bySlot, others) = await timetable.ClashesForAsync(@class, body.TermId, drafts, ct);
            var clashes = bySlot.Values.SelectMany(c => c).ToList();
            if (clashes.Count > 0)
            {
                var described = await timetable.DescribeAsync(@class.OrganizationId, clashes, others, ct);
                return ApiProblems.Conflict(ProblemCodes.timetable_conflict, extra: new { conflicts = described.DistinctBy(c => (c.Kind, c.SlotId)).ToList() });
            }
        }

        foreach (var slot in drafts)
        {
            slot.Publish();
        }

        await db.SaveChangesAsync(ct);
        var queued = drafts.Count > 0 && await queue.QueueAsync(@class.OrganizationId, id, body.TermId, ct);
        return Results.Ok(new PublishTimetableResultDto(drafts.Count, queued));
    }

    private static async Task<(Class? Class, IResult? Refused)> OpenAsync(HttpContext http, TimetableService timetable, Guid classId, string permission, CancellationToken ct)
    {
        var @class = await timetable.FindClassAsync(classId, ct);
        if (@class is null)
        {
            return (null, ApiProblems.NotFound());
        }

        var access = await http.CheckUnitAsync(permission, @class.OrgUnitId, ct);
        return access.Problem is not null ? (null, access.Problem) : (@class, null);
    }

    /// <summary>Checks the references of a save: the class course of the class, the term of its year, a period of the unit, a teacher of the course.</summary>
    private static async Task<IResult?> ValidateRequestAsync(
        Class @class, SaveSlotRequest? body, TimetableService timetable, PeriodService periods, LmsDbContext db, CancellationToken ct)
    {
        if (body is null || (body.RoomName is { } room && room.Trim().Length > TimetableLimits.MaxRoomLength))
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "The room is at most 100 characters.");
        }

        if (!await db.Set<ClassCourse>().AnyAsync(cc => cc.Id == body.ClassCourseId && cc.ClassId == @class.Id, ct)
            || await timetable.FindTermAsync(@class, body.TermId, ct) is null)
        {
            return ApiProblems.NotFound();
        }

        var unit = await db.OrgUnits.SingleAsync(u => u.Id == @class.OrgUnitId, ct);
        var (periodRows, _) = await periods.EffectiveAsync(unit, ct);
        if (periodRows.All(p => p.Id != body.PeriodId))
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "The period is not one of the unit's periods.");
        }

        return await timetable.TeachesAsync(body.ClassCourseId, body.TeacherUserId, ct)
            ? null
            : ApiProblems.BadRequest(ProblemCodes.validation_failed, "The teacher is not assigned to this class course.");
    }

    private static async Task<IResult> SaveAsync(
        LmsDbContext db, TimetableService timetable, ISettingsResolver settings, Class @class, TimetableSlot slot, bool isNew, CancellationToken ct)
    {
        var candidates = await timetable.ClashCandidatesAsync(@class, slot.TermId, slot, ct);
        var clashes = TimetableService.Clashes(slot, candidates);
        var mode = (await settings.ResolveAsync(SettingKeys.TimetableConflictMode, @class.OrgUnitPath, ct)).Value;
        var described = await timetable.DescribeAsync(@class.OrganizationId, clashes, candidates, ct);
        if (clashes.Count > 0 && mode != ConflictModes.Warn)
        {
            db.ChangeTracker.Clear();
            return ApiProblems.Conflict(ProblemCodes.timetable_conflict, extra: new { conflicts = described });
        }

        if (isNew)
        {
            db.Set<TimetableSlot>().Add(slot);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return ApiProblems.Conflict(ProblemCodes.concurrency_conflict);
        }
        catch (DbUpdateException e) when (e.InnerException is MySqlConnector.MySqlException { Number: 1062 })
        {
            // The unique live key (duplicate entry): the same class course already has a slot at this day and period. Any other database error is a 500.
            db.ChangeTracker.Clear();
            return ApiProblems.Conflict(ProblemCodes.timetable_conflict, extra: new { conflicts = described });
        }

        var dto = (await timetable.ToDtosAsync(@class.OrganizationId, [slot], clashes.Count > 0 ? new HashSet<Guid> { slot.Id } : null, ct))[0];
        var result = new SaveSlotResultDto(dto, described);
        return isNew ? Results.Created(Routes.ClassTimetableSlot(@class.Id, slot.Id), result) : Results.Ok(result);
    }

    private static async Task<IResult> StaleAsync(TimetableService timetable, TimetableSlot slot, CancellationToken ct)
    {
        var current = (await timetable.ToDtosAsync(slot.OrganizationId, [slot], null, ct))[0];
        return ApiProblems.Conflict(ProblemCodes.concurrency_conflict, extra: new { current });
    }

    private static async Task<HashSet<Guid>> ConflictingIdsAsync(TimetableService timetable, Class @class, Guid termId, IReadOnlyList<TimetableSlot> slots, CancellationToken ct)
    {
        var (bySlot, _) = await timetable.ClashesForAsync(@class, termId, slots, ct);
        return bySlot.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).ToHashSet();
    }

    private static string? Room(string? room) => string.IsNullOrWhiteSpace(room) ? null : room.Trim();
}
