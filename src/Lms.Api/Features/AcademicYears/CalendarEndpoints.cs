using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.AcademicYears;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.AcademicYears;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.AcademicYears;

public static class CalendarEndpoints
{
    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.AcademicYearCalendarPattern, GetAsync).RequireAuthorization()
            .RequirePermission(Permissions.AcademicYears.View);
        app.MapPost(Routes.CalendarEvents, CreateAsync).RequireAuthorization()
            .RequirePermission(Permissions.AcademicYears.Update);
        app.MapPut(Routes.CalendarEventPattern, UpdateAsync).RequireAuthorization()
            .RequirePermission(Permissions.AcademicYears.Update);
        app.MapDelete(Routes.CalendarEventPattern, DeleteAsync).RequireAuthorization()
            .RequirePermission(Permissions.AcademicYears.Update);
        return app;
    }

    private static async Task<IResult> GetAsync(
        Guid id, Guid? orgUnitId, HttpContext http, LmsDbContext db, ICurrentContext context, CancellationToken ct)
    {
        var unitId = orgUnitId;
        if (unitId is null && context.ScopePath is { } scope)
        {
            unitId = await db.OrgUnits.Where(u => u.Path == scope).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        }

        if (unitId is null)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "orgUnitId is required.");
        }

        var access = await http.CheckUnitAsync(Permissions.AcademicYears.View, unitId.Value, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        if (await FindYearForAsync(db, id, unit, ct) is null)
        {
            return ApiProblems.NotFound();
        }

        var chain = OrgUnitPath.AncestorsAndSelf(unit.Path);

        // ISOLATION-BYPASS: inherited events live at ancestors outside the caller's subtree filter; bound to the exact
        // ancestor paths of a unit the caller can see (and to the year, which is itself an ancestor-or-self year).
        var events = await db.Set<CalendarEvent>().IgnoreQueryFilters()
            .Where(e => !e.IsDeleted && e.OrganizationId == unit.OrganizationId && e.AcademicYearId == id && chain.Contains(e.OrgUnitPath))
            .OrderBy(e => e.StartDate).ThenBy(e => e.Id)
            .ToListAsync(ct);
        return Results.Ok(await ToDtosAsync(db, events, unit.Id, ct));
    }

    private static async Task<IResult> CreateAsync(
        SaveCalendarEventRequest request, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        if (Validate(request.TitleAr, request.TitleEn, request.Kind, request.StartDate, request.EndDate, out var kind) is { } invalid)
        {
            return invalid;
        }

        var access = await http.CheckUnitAsync(Permissions.AcademicYears.Update, request.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        var year = await FindYearForAsync(db, request.AcademicYearId, unit, ct);
        if (year is null)
        {
            return ApiProblems.NotFound();
        }

        if (CheckYear(year, request.StartDate, request.EndDate) is { } conflict)
        {
            return conflict;
        }

        var entity = new CalendarEvent(year, unit, request.TitleAr.Trim(), request.TitleEn.Trim(), kind, request.StartDate, request.EndDate);
        db.Set<CalendarEvent>().Add(entity);
        await db.SaveChangesAsync(ct);
        return Results.Created(Routes.CalendarEvent(entity.Id), (await ToDtosAsync(db, [entity], unit.Id, ct))[0]);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, SaveCalendarEventRequest request, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        if (Validate(request.TitleAr, request.TitleEn, request.Kind, request.StartDate, request.EndDate, out var kind) is { } invalid)
        {
            return invalid;
        }

        var (entity, year, problem) = await LoadAsync(id, http, db, ct);
        if (entity is null || year is null)
        {
            return problem!;
        }

        if (CheckYear(year, request.StartDate, request.EndDate) is { } conflict)
        {
            return conflict;
        }

        entity.Update(request.TitleAr.Trim(), request.TitleEn.Trim(), kind, request.StartDate, request.EndDate);
        await db.SaveChangesAsync(ct);
        return Results.Ok((await ToDtosAsync(db, [entity], entity.OrgUnitId, ct))[0]);
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var (entity, year, problem) = await LoadAsync(id, http, db, ct);
        if (entity is null || year is null)
        {
            return problem!;
        }

        if (year.Status == AcademicYearStatus.Closed)
        {
            return ApiProblems.Conflict(ProblemCodes.year_closed, "A closed academic year is read-only.");
        }

        db.Set<CalendarEvent>().Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>An event the caller can see (else 404), the permission checked at the event's own unit, and its year.</summary>
    private static async Task<(CalendarEvent? Event, AcademicYear? Year, IResult? Problem)> LoadAsync(
        Guid id, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var entity = await db.Set<CalendarEvent>().SingleOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null)
        {
            return (null, null, ApiProblems.NotFound());
        }

        var access = await http.CheckUnitAsync(Permissions.AcademicYears.Update, entity.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return (null, null, access.Problem);
        }

        var year = await FindYearForAsync(db, entity.AcademicYearId, access.Unit!, ct);
        return year is null ? (null, null, ApiProblems.NotFound()) : (entity, year, null);
    }

    /// <summary>The year, if it is defined at the unit or one of its ancestors (the years the unit effectively has).</summary>
    private static async Task<AcademicYear?> FindYearForAsync(LmsDbContext db, Guid yearId, OrgUnit unit, CancellationToken ct)
    {
        var chain = OrgUnitPath.AncestorsAndSelf(unit.Path);

        // ISOLATION-BYPASS: the year may be inherited from an ancestor outside the caller's subtree filter; restricted to
        // the same client and to the unit's own ancestor paths.
        return await db.Set<AcademicYear>().IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                y => y.Id == yearId && !y.IsDeleted && y.OrganizationId == unit.OrganizationId && chain.Contains(y.OrgUnitPath), ct);
    }

    private static IResult? CheckYear(AcademicYear year, DateOnly start, DateOnly end)
    {
        if (year.Status == AcademicYearStatus.Closed)
        {
            return ApiProblems.Conflict(ProblemCodes.year_closed, "A closed academic year is read-only.");
        }

        return start < year.StartDate || end > year.EndDate
            ? ApiProblems.Conflict(ProblemCodes.event_outside_year, "An event must lie within its academic year.")
            : null;
    }

    private static IResult? Validate(string? titleAr, string? titleEn, string? kindText, DateOnly start, DateOnly end, out CalendarEventKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(titleAr) || titleAr.Length > 200 || string.IsNullOrWhiteSpace(titleEn) || titleEn.Length > 200)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "TitleAr and TitleEn are required (max 200 characters).");
        }

        if (kindText is null || !Enum.TryParse(kindText, ignoreCase: false, out kind) || !Enum.IsDefined(kind))
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "Kind must be Holiday or Event.");
        }

        return start <= end ? null : ApiProblems.BadRequest(ProblemCodes.validation_failed, "StartDate must not be after EndDate.");
    }

    private static async Task<IReadOnlyList<CalendarEventDto>> ToDtosAsync(
        LmsDbContext db, IReadOnlyList<CalendarEvent> events, Guid viewedUnitId, CancellationToken ct)
    {
        var unitIds = events.Select(e => e.OrgUnitId).Distinct().ToList();
        var organizationId = events.Count > 0 ? events[0].OrganizationId : Guid.Empty;

        // ISOLATION-BYPASS: names of the (possibly ancestor) units that define events the caller may already see.
        var units = await db.OrgUnits.IgnoreQueryFilters()
            .Where(u => !u.IsDeleted && u.OrganizationId == organizationId && unitIds.Contains(u.Id))
            .Select(u => new { u.Id, u.NameAr, u.NameEn })
            .ToDictionaryAsync(u => u.Id, ct);

        return events.Select(e =>
        {
            units.TryGetValue(e.OrgUnitId, out var unit);
            return new CalendarEventDto(
                e.Id, e.AcademicYearId, e.OrgUnitId, unit?.NameAr ?? string.Empty, unit?.NameEn ?? string.Empty,
                e.TitleAr, e.TitleEn, e.Kind.ToString(), e.StartDate, e.EndDate, e.OrgUnitId != viewedUnitId);
        }).ToList();
    }
}
