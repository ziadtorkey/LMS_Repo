using Lms.Api.Infrastructure;
using Lms.Domain.AcademicYears;
using Lms.Domain.Abstractions;
using Lms.Infrastructure.AcademicYears;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.AcademicYears;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.AcademicYears;

public static class TermEndpoints
{
    public static IEndpointRouteBuilder MapTermEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.AcademicYearTermsPattern, CreateAsync).RequireAuthorization()
            .RequirePermission(Permissions.AcademicYears.Update);
        app.MapPut(Routes.TermPattern, UpdateAsync).RequireAuthorization()
            .RequirePermission(Permissions.AcademicYears.Update);
        app.MapDelete(Routes.TermPattern, DeleteAsync).RequireAuthorization()
            .RequirePermission(Permissions.AcademicYears.Update);
        app.MapGet(Routes.AcademicYearTermStructurePattern, GetStructureAsync).RequireAuthorization()
            .RequirePermission(Permissions.AcademicYears.View);
        app.MapPut(Routes.AcademicYearTermStructurePattern, ApplyStructureAsync).RequireAuthorization()
            .RequirePermission(Permissions.AcademicYears.Update);
        return app;
    }

    private static async Task<IResult> GetStructureAsync(
        Guid id, HttpContext http, LmsDbContext db, TermStructureService structure, CancellationToken ct)
    {
        var (year, problem) = await AcademicYearEndpoints.LoadForAsync(Permissions.AcademicYears.View, id, http, db, ct);
        return year is null ? problem! : Results.Ok(await structure.GetAsync(year, ct));
    }

    private static async Task<IResult> ApplyStructureAsync(
        Guid id, TermStructureDto? request, HttpContext http, LmsDbContext db, TermStructureService structure, CancellationToken ct)
    {
        var (year, problem) = await AcademicYearEndpoints.LoadForAsync(Permissions.AcademicYears.Update, id, http, db, ct);
        if (year is null)
        {
            return problem!;
        }

        if (request is null || !TermTemplate.IsValid(request.TermsPerYear))
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "TermsPerYear must be 2 or 3.");
        }

        return await structure.ApplyAsync(year, request.TermsPerYear, ct) switch
        {
            TermStructureOutcome.Closed => ApiProblems.Conflict(ProblemCodes.year_closed, "A closed academic year is read-only."),
            TermStructureOutcome.NotPlanned => ApiProblems.Conflict(ProblemCodes.year_active, "Only a planned year can change its term structure."),
            TermStructureOutcome.HasDependants => ApiProblems.Conflict(ProblemCodes.terms_have_dependants, "A term already has a gradebook, timetable slot or attendance session."),
            TermStructureOutcome.NoRoom => ApiProblems.Conflict(ProblemCodes.term_outside_year, "The year has no room left for the missing terms."),
            _ => Results.Ok(new TermStructureDto(request.TermsPerYear, TermTemplate.NamesFor(request.TermsPerYear))),
        };
    }

    private static async Task<IResult> CreateAsync(
        Guid id, SaveTermRequest request, HttpContext http, LmsDbContext db, IClock clock, CancellationToken ct)
    {
        if (AcademicYearValidators.Validate(request) is { } invalid)
        {
            return invalid;
        }

        var (year, problem) = await AcademicYearEndpoints.LoadForAsync(Permissions.AcademicYears.Update, id, http, db, ct);
        if (year is null)
        {
            return problem!;
        }

        var terms = await db.Set<Term>().Where(t => t.AcademicYearId == year.Id).ToListAsync(ct);
        if (Check(year, terms, null, request) is { } conflict)
        {
            return conflict;
        }

        var term = new Term(year, request.NameAr.Trim(), request.NameEn.Trim(), request.StartDate, request.EndDate);
        db.Set<Term>().Add(term);
        terms.Add(term);
        Reorder(terms);
        await db.SaveChangesAsync(ct);
        return Results.Created(Routes.Term(term.Id), AcademicYearMapper.ToDto(term, AcademicYearEndpoints.Today(clock)));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, SaveTermRequest request, HttpContext http, LmsDbContext db, IClock clock, CancellationToken ct)
    {
        if (AcademicYearValidators.Validate(request) is { } invalid)
        {
            return invalid;
        }

        var (term, year, problem) = await LoadAsync(id, http, db, ct);
        if (term is null || year is null)
        {
            return problem!;
        }

        var terms = await db.Set<Term>().Where(t => t.AcademicYearId == year.Id).ToListAsync(ct);
        if (Check(year, terms, term.Id, request) is { } conflict)
        {
            return conflict;
        }

        term.Update(request.NameAr.Trim(), request.NameEn.Trim(), request.StartDate, request.EndDate);
        Reorder(terms);
        await db.SaveChangesAsync(ct);
        return Results.Ok(AcademicYearMapper.ToDto(term, AcademicYearEndpoints.Today(clock)));
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var (term, year, problem) = await LoadAsync(id, http, db, ct);
        if (term is null || year is null)
        {
            return problem!;
        }

        if (year.Status == AcademicYearStatus.Closed)
        {
            return ApiProblems.Conflict(ProblemCodes.year_closed, "A closed academic year is read-only.");
        }

        var remaining = await db.Set<Term>().Where(t => t.AcademicYearId == year.Id && t.Id != term.Id).ToListAsync(ct);
        db.Set<Term>().Remove(term);
        Reorder(remaining);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<(Term? Term, AcademicYear? Year, IResult? Problem)> LoadAsync(
        Guid termId, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var term = await db.Set<Term>().SingleOrDefaultAsync(t => t.Id == termId, ct);
        if (term is null)
        {
            return (null, null, ApiProblems.NotFound());
        }

        var (year, problem) = await AcademicYearEndpoints.LoadForAsync(Permissions.AcademicYears.Update, term.AcademicYearId, http, db, ct);
        return year is null ? (null, null, problem) : (term, year, null);
    }

    private static IResult? Check(AcademicYear year, List<Term> terms, Guid? selfId, SaveTermRequest request)
    {
        if (year.Status == AcademicYearStatus.Closed)
        {
            return ApiProblems.Conflict(ProblemCodes.year_closed, "A closed academic year is read-only.");
        }

        if (request.StartDate < year.StartDate || request.EndDate > year.EndDate)
        {
            return ApiProblems.Conflict(ProblemCodes.term_outside_year, "A term must lie within its academic year.");
        }

        return terms.Any(t => t.Id != selfId && t.Overlaps(request.StartDate, request.EndDate))
            ? ApiProblems.Conflict(ProblemCodes.term_overlap, "Another term of this year overlaps these dates.")
            : null;
    }

    /// <summary>Order always follows the start date (1-based).</summary>
    private static void Reorder(List<Term> terms)
    {
        var order = 1;
        foreach (var term in terms.OrderBy(t => t.StartDate))
        {
            term.SetOrder(order++);
        }
    }
}
