using Lms.Api.Infrastructure;
using Lms.Domain.AcademicYears;
using Lms.Domain.Abstractions;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.AcademicYears;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.AcademicYears;

public static class AcademicYearEndpoints
{
    public static IEndpointRouteBuilder MapAcademicYearEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.AcademicYearsEffective, EffectiveAsync).RequireAuthorization()
            .RequirePermission(Permissions.AcademicYears.View);
        app.MapPost(Routes.AcademicYears, CreateAsync).RequireAuthorization()
            .RequirePermission(Permissions.AcademicYears.Create);
        app.MapPut(Routes.AcademicYearPattern, UpdateAsync).RequireAuthorization()
            .RequirePermission(Permissions.AcademicYears.Update);
        app.MapPost(Routes.AcademicYearActivatePattern, ActivateAsync).RequireAuthorization()
            .RequirePermission(Permissions.AcademicYears.Update);
        app.MapDelete(Routes.AcademicYearPattern, DeleteAsync).RequireAuthorization()
            .RequirePermission(Permissions.AcademicYears.Delete);
        return app;
    }

    private static async Task<IResult> EffectiveAsync(
        Guid? orgUnitId, HttpContext http, LmsDbContext db, ICurrentContext context, IAcademicYearResolver resolver,
        IClock clock, CancellationToken ct)
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
        var (years, definingId) = await resolver.GetEffectiveAsync(unit.Path, ct);
        var dtos = await AcademicYearMapper.ToDtosAsync(db, years, unit.Id, Today(clock), ct);
        var current = years.FirstOrDefault(y => y.Status == AcademicYearStatus.Active);
        return Results.Ok(new YearContextDto(dtos, current?.Id, definingId));
    }

    private static async Task<IResult> CreateAsync(
        SaveAcademicYearRequest request, HttpContext http, LmsDbContext db, IClock clock, CancellationToken ct)
    {
        if (AcademicYearValidators.Validate(request) is { } invalid)
        {
            return invalid;
        }

        var access = await http.CheckUnitAsync(Permissions.AcademicYears.Create, request.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        if (await db.Set<AcademicYear>().AnyAsync(y => y.OrgUnitId == unit.Id && y.StartDate <= request.EndDate && request.StartDate <= y.EndDate, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.year_overlap, "Another academic year of this unit overlaps these dates.");
        }

        var year = new AcademicYear(unit, request.NameAr.Trim(), request.NameEn.Trim(), request.StartDate, request.EndDate);
        db.Set<AcademicYear>().Add(year);
        await db.SaveChangesAsync(ct);
        return Results.Created(Routes.AcademicYear(year.Id), await ToDtoAsync(db, year, clock, ct));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, SaveAcademicYearRequest request, HttpContext http, LmsDbContext db, IClock clock, CancellationToken ct)
    {
        if (AcademicYearValidators.Validate(request) is { } invalid)
        {
            return invalid;
        }

        var (year, problem) = await LoadForAsync(Permissions.AcademicYears.Update, id, http, db, ct);
        if (year is null)
        {
            return problem!;
        }

        if (year.Status == AcademicYearStatus.Closed)
        {
            return ApiProblems.Conflict(ProblemCodes.year_closed, "A closed academic year is read-only.");
        }

        if (await db.Set<AcademicYear>().AnyAsync(
                y => y.OrgUnitId == year.OrgUnitId && y.Id != year.Id && y.StartDate <= request.EndDate && request.StartDate <= y.EndDate, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.year_overlap, "Another academic year of this unit overlaps these dates.");
        }

        if (await db.Set<Term>().AnyAsync(t => t.AcademicYearId == year.Id && (t.StartDate < request.StartDate || t.EndDate > request.EndDate), ct))
        {
            return ApiProblems.Conflict(ProblemCodes.term_outside_year, "A term of this year would fall outside the new dates.");
        }

        year.Update(request.NameAr.Trim(), request.NameEn.Trim(), request.StartDate, request.EndDate);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToDtoAsync(db, year, clock, ct));
    }

    private static async Task<IResult> ActivateAsync(
        Guid id, HttpContext http, LmsDbContext db, IClock clock, CancellationToken ct)
    {
        var (year, problem) = await LoadForAsync(Permissions.AcademicYears.Update, id, http, db, ct);
        if (year is null)
        {
            return problem!;
        }

        if (year.Status == AcademicYearStatus.Closed)
        {
            return ApiProblems.Conflict(ProblemCodes.year_closed, "A closed academic year cannot be activated.");
        }

        if (year.Status != AcademicYearStatus.Active)
        {
            if (await db.Set<AcademicYear>().AnyAsync(y => y.OrgUnitId == year.OrgUnitId && y.Id != year.Id && y.Status == AcademicYearStatus.Active, ct))
            {
                return ApiProblems.Conflict(ProblemCodes.active_year_exists, "Another academic year of this unit is already active.");
            }

            year.Activate();
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(await ToDtoAsync(db, year, clock, ct));
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var (year, problem) = await LoadForAsync(Permissions.AcademicYears.Delete, id, http, db, ct);
        if (year is null)
        {
            return problem!;
        }

        if (year.Status == AcademicYearStatus.Active)
        {
            return ApiProblems.Conflict(ProblemCodes.year_active, "An active academic year cannot be deleted.");
        }

        if (year.Status == AcademicYearStatus.Closed)
        {
            return ApiProblems.Conflict(ProblemCodes.year_closed, "A closed academic year cannot be deleted.");
        }

        db.Set<Term>().RemoveRange(await db.Set<Term>().Where(t => t.AcademicYearId == year.Id).ToListAsync(ct));
        db.Set<AcademicYear>().Remove(year);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Loads a year the caller can see (else 404) and checks the permission at the year's own unit.</summary>
    internal static async Task<(AcademicYear? Year, IResult? Problem)> LoadForAsync(
        string permission, Guid yearId, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var year = await db.Set<AcademicYear>().SingleOrDefaultAsync(y => y.Id == yearId, ct);
        if (year is null)
        {
            return (null, ApiProblems.NotFound());
        }

        var access = await http.CheckUnitAsync(permission, year.OrgUnitId, ct);
        return access.Problem is not null ? (null, access.Problem) : (year, null);
    }

    internal static async Task<AcademicYearDto> ToDtoAsync(LmsDbContext db, AcademicYear year, IClock clock, CancellationToken ct) =>
        (await AcademicYearMapper.ToDtosAsync(db, [year], year.OrgUnitId, Today(clock), ct))[0];

    internal static DateOnly Today(IClock clock) => DateOnly.FromDateTime(clock.UtcNow);
}
