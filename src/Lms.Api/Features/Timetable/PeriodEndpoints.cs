using Lms.Api.Infrastructure;
using Lms.Domain.Organizations;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Persistence;
using Lms.Infrastructure.Timetable;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Timetable;

/// <summary>Periods of a unit's school day (FR-TT-01): read with Timetable.View, written with Timetable.Configure; unit access decides, a miss is 404.</summary>
public static class PeriodEndpoints
{
    private const int MaxOrder = 20;

    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.OrgUnitPeriodsPattern, ListAsync).RequirePermission(Permissions.Timetable.View);
        app.MapPost(Routes.OrgUnitPeriodsPattern, CreateAsync).RequirePermission(Permissions.Timetable.Configure);
        app.MapPut(Routes.PeriodPattern, UpdateAsync).RequirePermission(Permissions.Timetable.Configure);
        app.MapDelete(Routes.PeriodPattern, DeleteAsync).RequirePermission(Permissions.Timetable.Configure);
        return app;
    }

    private static async Task<IResult> ListAsync(Guid id, HttpContext http, PeriodService periods, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Timetable.View, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var (rows, inheritedFrom) = await periods.EffectiveAsync(access.Unit!, ct);
        return Results.Ok(rows.Select(p => ToDto(p, inheritedFrom)).ToList());
    }

    private static async Task<IResult> CreateAsync(
        Guid id, SavePeriodRequest? body, HttpContext http, LmsDbContext db, PeriodService periods, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Timetable.Configure, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        if (Validate(body) is { } invalid)
        {
            return invalid;
        }

        var unit = access.Unit!;
        if (await periods.CountOwnAsync(unit.Id, ct) >= TimetableLimits.MaxPeriodsPerUnit)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, $"A unit has at most {TimetableLimits.MaxPeriodsPerUnit} periods.");
        }

        if (await periods.OverlapsAsync(unit.Id, body!.StartTime, body.EndTime, null, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.period_overlap);
        }

        var period = new Period(unit, body.NameAr.Trim(), body.NameEn.Trim(), body.StartTime, body.EndTime, body.Order);
        db.Set<Period>().Add(period);
        await db.SaveChangesAsync(ct);
        return Results.Created(Routes.Period(unit.Id, period.Id), ToDto(period, null));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, Guid pid, SavePeriodRequest? body, HttpContext http, LmsDbContext db, PeriodService periods, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Timetable.Configure, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        if (Validate(body) is { } invalid)
        {
            return invalid;
        }

        // Only the unit's own periods: an inherited period is read only and does not exist at the descendant (404).
        var period = await db.Set<Period>().SingleOrDefaultAsync(p => p.Id == pid && p.OrgUnitId == id, ct);
        if (period is null)
        {
            return ApiProblems.NotFound();
        }

        if (await periods.OverlapsAsync(id, body!.StartTime, body.EndTime, pid, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.period_overlap);
        }

        period.Update(body.NameAr.Trim(), body.NameEn.Trim(), body.StartTime, body.EndTime, body.Order);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(period, null));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, Guid pid, HttpContext http, LmsDbContext db, IPeriodUsage usage, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Timetable.Configure, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var period = await db.Set<Period>().SingleOrDefaultAsync(p => p.Id == pid && p.OrgUnitId == id, ct);
        if (period is null)
        {
            return ApiProblems.NotFound();
        }

        if (await usage.IsUsedAsync(pid, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.period_in_use);
        }

        db.Set<Period>().Remove(period);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static IResult? Validate(SavePeriodRequest? body)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.NameAr) || string.IsNullOrWhiteSpace(body.NameEn)
            || body.NameAr.Trim().Length > TimetableLimits.MaxNameLength || body.NameEn.Trim().Length > TimetableLimits.MaxNameLength)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "Names are required, 1-100 characters.");
        }

        if (body.StartTime >= body.EndTime)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "The start time must be before the end time.");
        }

        return body.Order is < 1 or > MaxOrder
            ? ApiProblems.BadRequest(ProblemCodes.validation_failed, $"The order is 1-{MaxOrder}.")
            : null;
    }

    private static PeriodDto ToDto(Period p, Guid? inheritedFrom) =>
        new(p.Id, p.OrgUnitId, p.NameAr, p.NameEn, p.StartTime, p.EndTime, p.Order, inheritedFrom);
}
