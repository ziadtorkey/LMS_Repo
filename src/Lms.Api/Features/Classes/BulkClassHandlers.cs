using Lms.Api.Infrastructure;
using Lms.Domain.AcademicYears;
using Lms.Domain.Classes;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Classes;

/// <summary>POST /api/classes/bulk and /api/classes/copy: many classes in one transaction.</summary>
public sealed class BulkClassHandlers(
    LmsDbContext db, ClassHandlers single, ClassLookups lookups, ClassMapper mapper, IAcademicYearResolver years,
    ClassCourseWriter courseWriter)
{
    public async Task<IResult> BulkAsync(BulkCreateClassesRequest request, HttpContext http, CancellationToken ct)
    {
        if (ClassValidators.Validate(request) is { } invalid)
        {
            return invalid;
        }

        if (request.Capacity is < 1 or > 500)
        {
            return ClassValidators.Invalid(nameof(request.Capacity), "capacity_invalid");
        }

        var access = await http.CheckUnitAsync(Permissions.Classes.Create, request.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        if (await single.CheckHostAsync(unit, request.AcademicYearId, ct) is { } problem)
        {
            return problem;
        }

        var rendered = Enumerable.Range(request.From, request.To - request.From + 1)
            .Select(n => (Code: ClassValidators.Render(request.CodePattern, n).Trim().ToUpperInvariant(),
                NameAr: ClassValidators.Render(request.NamePatternAr, n).Trim(), NameEn: ClassValidators.Render(request.NamePatternEn, n).Trim()))
            .ToList();
        if (rendered.Any(r => !ClassValidators.IsValidCode(r.Code) || !ClassValidators.ValidName(r.NameAr) || !ClassValidators.ValidName(r.NameEn)))
        {
            return ClassValidators.Invalid(nameof(request.CodePattern), "rendered_value_invalid");
        }

        var taken = await lookups.TakenCodesAsync(unit.OrganizationId, request.AcademicYearId, rendered.Select(r => r.Code).ToList(), null, ct);
        if (taken.Count > 0)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict, title: "Conflict",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = ProblemCodes.class_code_taken, ["codes"] = taken.Distinct().Order().ToList(),
                });
        }

        var created = rendered.Select(r => new Class(unit, request.AcademicYearId, r.Code, r.NameAr, r.NameEn, request.Capacity)).ToList();
        db.AddRange(created);
        await db.SaveChangesAsync(ct);
        return Results.Created(Routes.Classes, await mapper.ToDtosAsync(created, ct));
    }

    public async Task<IResult> CopyAsync(CopyClassesRequest request, HttpContext http, CancellationToken ct)
    {
        if (request is null)
        {
            return ClassValidators.Invalid("body", "required");
        }

        var access = await http.CheckUnitAsync(Permissions.Classes.Create, request.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        var yearsById = await lookups.YearsAsync(unit.OrganizationId, [request.SourceYearId, request.TargetYearId], ct);
        if (!yearsById.ContainsKey(request.SourceYearId) || !yearsById.TryGetValue(request.TargetYearId, out var target))
        {
            return ApiProblems.NotFound();
        }

        if (target.Status == AcademicYearStatus.Closed)
        {
            return ApiProblems.Conflict(ProblemCodes.year_closed, "The target academic year is closed.");
        }

        var path = unit.Path;
        var sources = await db.Set<Class>()
            .Where(c => c.OrgUnitPath.StartsWith(path) && c.AcademicYearId == request.SourceYearId)
            .OrderBy(c => c.Code).Take(ClassValidators.MaxCopy + 1).ToListAsync(ct);
        if (sources.Count > ClassValidators.MaxCopy)
        {
            return ClassValidators.Invalid(nameof(request.SourceYearId), "too_many_classes");
        }

        var from = request.ReplaceFrom;
        var to = request.ReplaceTo ?? string.Empty;
        string Replace(string value) => string.IsNullOrEmpty(from) ? value : value.Replace(from, to);

        var plans = sources.Select(c => (Source: c, Code: Replace(c.Code).Trim().ToUpperInvariant(), NameAr: Replace(c.NameAr).Trim(), NameEn: Replace(c.NameEn).Trim())).ToList();
        var existing = (await lookups.TakenCodesAsync(unit.OrganizationId, request.TargetYearId, plans.Select(p => p.Code).Distinct().ToList(), null, ct)).ToHashSet();
        var effectiveAt = new Dictionary<Guid, bool>();
        var skipped = new List<string>();
        var created = new List<Class>();
        var copies = new List<(Class Source, Class Copy, Domain.Organizations.OrgUnit Unit)>();
        var seen = new HashSet<string>();
        foreach (var (source, code, nameAr, nameEn) in plans)
        {
            if (!effectiveAt.TryGetValue(source.OrgUnitId, out var applies))
            {
                applies = (await years.GetEffectiveAsync(source.OrgUnitPath, ct)).Years.Any(y => y.Id == request.TargetYearId);
                effectiveAt[source.OrgUnitId] = applies;
            }

            if (!applies)
            {
                continue; // the target year does not apply at that unit: nothing to copy there
            }

            if (!ClassValidators.IsValidCode(code) || !ClassValidators.ValidName(nameAr) || !ClassValidators.ValidName(nameEn)
                || existing.Contains(code) || !seen.Add(code))
            {
                skipped.Add(code);
                continue;
            }

            var classUnit = await db.OrgUnits.FirstAsync(u => u.Id == source.OrgUnitId, ct);
            var copy = new Class(classUnit, request.TargetYearId, code, nameAr, nameEn, source.Capacity);
            copy.Update(code, nameAr, nameEn, source.Capacity, source.HomeroomTeacherUserId);
            created.Add(copy);
            copies.Add((source, copy, classUnit));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.AddRange(created);
        await db.SaveChangesAsync(ct);
        if (request.IncludeCourses)
        {
            await CopyCoursesAsync(copies, ct);
        }

        await transaction.CommitAsync(ct);
        return Results.Ok(new CopyClassesResultDto(created.Count, skipped));
    }

    /// <summary>Copies each source class's courses (and optional flags) that are still available at the class unit.</summary>
    private async Task CopyCoursesAsync(List<(Class Source, Class Copy, Domain.Organizations.OrgUnit Unit)> copies, CancellationToken ct)
    {
        foreach (var (source, copy, unit) in copies)
        {
            var rows = await db.Set<ClassCourse>().Where(c => c.ClassId == source.Id).ToListAsync(ct);
            var wanted = new List<(Domain.Courses.Course, bool)>();
            foreach (var row in rows)
            {
                if (await courseWriter.FindAvailableAsync(row.CourseId, unit, ct) is { } course)
                {
                    wanted.Add((course, row.IsOptional));
                }
            }

            await courseWriter.AddAsync(copy, wanted, ct);
        }
    }
}
