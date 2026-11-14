using Lms.Api.Infrastructure;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Persistence;
using Lms.Infrastructure.Timetable;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Timetable;

/// <summary>
/// Dated exceptions of a published slot: substitute, cancelled, moved (FR-TT-05, M5 Task 15). The slot is read through the unit-subtree filters, so one
/// outside the caller's scope is a 404; the permission is then checked at the slot's own unit. The base slot never changes.
/// </summary>
public static class ExceptionEndpoints
{
    public static IEndpointRouteBuilder MapExceptions(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.TimetableSlotExceptionsPattern, ListAsync).RequirePermission(Permissions.Timetable.View);
        app.MapPost(Routes.TimetableSlotExceptionsPattern, CreateAsync).RequirePermission(Permissions.Timetable.Manage);
        app.MapPut(Routes.TimetableSlotExceptionPattern, UpdateAsync).RequirePermission(Permissions.Timetable.Manage);
        app.MapDelete(Routes.TimetableSlotExceptionPattern, DeleteAsync).RequirePermission(Permissions.Timetable.Manage);
        return app;
    }

    private static async Task<IResult> ListAsync(Guid id, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var (slot, refused) = await OpenSlotAsync(http, db, id, Permissions.Timetable.View, ct);
        if (slot is null)
        {
            return refused!;
        }

        var rows = await db.Set<TimetableException>().Where(e => e.SlotId == id).OrderBy(e => e.Date).ToListAsync(ct);
        return Results.Ok(await ToDtosAsync(db, rows, ct));
    }

    private static async Task<IResult> CreateAsync(
        Guid id, SaveTimetableExceptionRequest? body, HttpContext http, LmsDbContext db, ExceptionService exceptions, CancellationToken ct)
    {
        var (slot, refused) = await OpenSlotAsync(http, db, id, Permissions.Timetable.Manage, ct);
        if (slot is null)
        {
            return refused!;
        }

        if (body is null)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "body: required");
        }

        var term = await TermOfAsync(db, slot, ct);
        var check = await exceptions.CheckAsync(slot, term, ToInput(body), null, ct);
        if (!check.IsValid)
        {
            return Problem(check);
        }

        var exception = new TimetableException(slot, body.Date, body.Kind, body.SubstituteUserId, body.NewPeriodId, Note(body.Note));
        db.Add(exception);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.ToString().Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase))
        {
            return ApiProblems.BadRequest(ProblemCodes.exception_date_invalid); // another request created the exception for this date first
        }

        return Results.Created(Routes.TimetableSlotException(id, exception.Id), (await ToDtosAsync(db, [exception], ct))[0]);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, Guid eid, SaveTimetableExceptionRequest? body, HttpContext http, LmsDbContext db, ExceptionService exceptions, CancellationToken ct)
    {
        var (slot, refused) = await OpenSlotAsync(http, db, id, Permissions.Timetable.Manage, ct);
        if (slot is null)
        {
            return refused!;
        }

        var exception = await db.Set<TimetableException>().SingleOrDefaultAsync(e => e.Id == eid && e.SlotId == id, ct);
        if (exception is null)
        {
            return ApiProblems.NotFound();
        }

        if (body is null || body.Date != exception.Date)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "The date of an exception cannot change: delete it and add another.");
        }

        var term = await TermOfAsync(db, slot, ct);
        var check = await exceptions.CheckAsync(slot, term, ToInput(body), exception.Id, ct);
        if (!check.IsValid)
        {
            return Problem(check);
        }

        exception.Apply(body.Kind, body.SubstituteUserId, body.NewPeriodId, Note(body.Note));
        await db.SaveChangesAsync(ct);
        return Results.Ok((await ToDtosAsync(db, [exception], ct))[0]);
    }

    private static async Task<IResult> DeleteAsync(Guid id, Guid eid, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var (slot, refused) = await OpenSlotAsync(http, db, id, Permissions.Timetable.Manage, ct);
        if (slot is null)
        {
            return refused!;
        }

        var exception = await db.Set<TimetableException>().SingleOrDefaultAsync(e => e.Id == eid && e.SlotId == id, ct);
        if (exception is null)
        {
            return ApiProblems.NotFound();
        }

        db.Remove(exception);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<(TimetableSlot? Slot, IResult? Refused)> OpenSlotAsync(HttpContext http, LmsDbContext db, Guid slotId, string permission, CancellationToken ct)
    {
        var slot = await db.Set<TimetableSlot>().SingleOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null)
        {
            return (null, ApiProblems.NotFound());
        }

        var access = await http.CheckUnitAsync(permission, slot.OrgUnitId, ct);
        return access.Problem is not null ? (null, access.Problem) : (slot, null);
    }

    private static Task<Lms.Domain.AcademicYears.Term> TermOfAsync(LmsDbContext db, TimetableSlot slot, CancellationToken ct) =>
        // ISOLATION-BYPASS: the term belongs to the class's academic year, which can be defined at an ancestor unit above the caller's scope; limited to the
        // slot's own client and term id, live rows only; only the term dates are read.
        db.Set<Lms.Domain.AcademicYears.Term>().IgnoreQueryFilters()
            .SingleAsync(t => !t.IsDeleted && t.OrganizationId == slot.OrganizationId && t.Id == slot.TermId, ct);

    private static ExceptionInput ToInput(SaveTimetableExceptionRequest b) => new(b.Date, b.Kind, b.SubstituteUserId, b.NewPeriodId, Note(b.Note));

    private static string? Note(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static IResult Problem(ExceptionCheck check) => check.Status == 409
        ? ApiProblems.Conflict(check.Problem!)
        : ApiProblems.BadRequest(check.Problem!);

    private static async Task<IReadOnlyList<TimetableExceptionDto>> ToDtosAsync(LmsDbContext db, IReadOnlyList<TimetableException> rows, CancellationToken ct)
    {
        var ids = rows.Where(r => r.SubstituteUserId is not null).Select(r => r.SubstituteUserId!.Value).Distinct().ToList();
        var names = ids.Count == 0
            ? []
            : await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayNameEn, ct);
        return rows.Select(r => new TimetableExceptionDto(
            r.Id, r.SlotId, r.Date, r.Kind, r.SubstituteUserId, r.SubstituteUserId is { } s ? names.GetValueOrDefault(s) : null, r.NewPeriodId, r.Note)).ToList();
    }
}
