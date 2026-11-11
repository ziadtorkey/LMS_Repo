using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.AcademicYears;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Classes;

/// <summary>Create, read, update, delete and close of single classes.</summary>
public sealed class ClassHandlers(
    LmsDbContext db, ICurrentContext context, IAcademicYearResolver years, IRelationshipScope relationships,
    ClassLookups lookups, ClassMapper mapper)
{
    public async Task<IResult> CreateAsync(SaveClassRequest request, HttpContext http, CancellationToken ct)
    {
        if (ClassValidators.Validate(request) is { } invalid)
        {
            return invalid;
        }

        var access = await http.CheckUnitAsync(Permissions.Classes.Create, request.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        if (await CheckHostAsync(unit, request.AcademicYearId, ct) is { } problem)
        {
            return problem;
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if ((await lookups.TakenCodesAsync(unit.OrganizationId, request.AcademicYearId, [code], null, ct)).Count > 0)
        {
            return ApiProblems.Conflict(ProblemCodes.class_code_taken);
        }

        if (await CheckHomeroomAsync(unit, request.HomeroomTeacherUserId, ct) is { } homeroom)
        {
            return homeroom;
        }

        var cls = new Class(unit, request.AcademicYearId, code, request.NameAr, request.NameEn, request.Capacity);
        cls.Update(code, request.NameAr, request.NameEn, request.Capacity, request.HomeroomTeacherUserId);
        db.Add(cls);
        await db.SaveChangesAsync(ct);
        return Results.Created(Routes.Class(cls.Id), await mapper.ToDtoAsync(cls, ct));
    }

    public async Task<IResult> ListAsync(ClassQuery query, HttpContext http, CancellationToken ct)
    {
        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > Paging.MaxPageSize)
        {
            return ClassValidators.Invalid(query.Page < 1 ? nameof(query.Page) : nameof(query.PageSize), "paging_invalid");
        }

        if (query.Status is not null && !ClassStatuses.All.Contains(query.Status))
        {
            return ClassValidators.Invalid(nameof(query.Status), "invalid_value");
        }

        var unitId = query.OrgUnitId;
        if (unitId is null && context.ScopePath is { } scope)
        {
            unitId = await db.OrgUnits.Where(u => u.Path == scope).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        }

        if (unitId is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Classes.View, unitId.Value, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        var yearId = query.AcademicYearId ?? (await years.GetCurrentAsync(unit.Path, ct))?.Id;
        if (yearId is null)
        {
            return Results.Ok(new PagedResult<ClassSummaryDto>([], 0, query.Page, query.PageSize));
        }

        var path = unit.Path;
        var classes = query.IncludeDescendants
            ? db.Set<Class>().Where(c => c.OrgUnitPath.StartsWith(path))
            : db.Set<Class>().Where(c => c.OrgUnitId == unit.Id);
        classes = classes.Where(c => c.AcademicYearId == yearId);
        if (query.Status is not null)
        {
            classes = classes.Where(c => c.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // LIKE follows the column collation (case-insensitive); wildcard characters typed by the user are escaped.
            var text = query.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            classes = classes.Where(c => EF.Functions.Like(c.Code, text + "%") || EF.Functions.Like(c.NameAr, "%" + text + "%")
                || EF.Functions.Like(c.NameEn, "%" + text + "%"));
        }

        var filter = await relationships.ForAsync(Permissions.Classes.View, ct);
        if (!filter.IsUnrestricted)
        {
            var allowed = filter.ClassIds;
            classes = classes.Where(c => allowed.Contains(c.Id));
        }

        var ordered = query.Sort switch
        {
            "-code" => classes.OrderByDescending(c => c.Code),
            "name" => classes.OrderBy(c => c.NameEn).ThenBy(c => c.Id),
            _ => classes.OrderBy(c => c.Code).ThenBy(c => c.Id),
        };
        var total = await ordered.CountAsync(ct);
        var page = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Results.Ok(new PagedResult<ClassSummaryDto>(await mapper.ToDtosAsync(page, ct), total, query.Page, query.PageSize));
    }

    public async Task<IResult> GetAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var (cls, problem) = await http.CheckClassAsync(Permissions.Classes.View, id, ct);
        return problem ?? Results.Ok(await mapper.ToDtoAsync(cls!, ct));
    }

    public async Task<IResult> UpdateAsync(Guid id, SaveClassRequest request, HttpContext http, CancellationToken ct)
    {
        var (cls, problem) = await http.CheckClassAsync(Permissions.Classes.Update, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await ClosedProblemAsync(cls!, ct) is { } closed)
        {
            return closed;
        }

        if (ClassValidators.Validate(request) is { } invalid)
        {
            return invalid;
        }

        // A class stays in its unit and year: moving it would change who sees it and which calendar applies.
        if (request.OrgUnitId != cls!.OrgUnitId || request.AcademicYearId != cls.AcademicYearId)
        {
            return ClassValidators.Invalid(request.OrgUnitId != cls.OrgUnitId ? nameof(request.OrgUnitId) : nameof(request.AcademicYearId), "cannot_move");
        }

        var active = await db.Set<ClassStudent>().CountAsync(s => s.ClassId == cls.Id && s.Status == ClassStudent.ActiveStatus, ct);
        if (request.Capacity is { } capacity && capacity < active)
        {
            return ClassValidators.Invalid(nameof(request.Capacity), "below_student_count");
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if ((await lookups.TakenCodesAsync(cls.OrganizationId, cls.AcademicYearId, [code], cls.Id, ct)).Count > 0)
        {
            return ApiProblems.Conflict(ProblemCodes.class_code_taken);
        }

        var unit = await db.OrgUnits.FirstAsync(u => u.Id == cls.OrgUnitId, ct);
        if (request.HomeroomTeacherUserId != cls.HomeroomTeacherUserId
            && await CheckHomeroomAsync(unit, request.HomeroomTeacherUserId, ct) is { } homeroom)
        {
            return homeroom;
        }

        cls.Update(code, request.NameAr, request.NameEn, request.Capacity, request.HomeroomTeacherUserId);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await mapper.ToDtoAsync(cls, ct));
    }

    public async Task<IResult> DeleteAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var (cls, problem) = await http.CheckClassAsync(Permissions.Classes.Delete, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await ClosedProblemAsync(cls!, ct) is { } closed)
        {
            return closed;
        }

        if (await db.Set<ClassStudent>().AnyAsync(s => s.ClassId == id, ct) || await db.Set<ClassCourse>().AnyAsync(c => c.ClassId == id, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.class_in_use);
        }

        db.Remove(cls!);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public async Task<IResult> CloseAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var (cls, problem) = await http.CheckClassAsync(Permissions.Classes.Close, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await ClosedProblemAsync(cls!, ct) is { } closed)
        {
            return closed;
        }

        cls!.SetStatus(Class.ClosedStatus);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await mapper.ToDtoAsync(cls, ct));
    }

    /// <summary>The unit hosts classes (level type) and the year applies to it and is not closed.</summary>
    internal async Task<IResult?> CheckHostAsync(OrgUnit unit, Guid yearId, CancellationToken ct)
    {
        if (!await db.LevelTypes.AnyAsync(l => l.Id == unit.LevelTypeId && l.CanHostClasses, ct))
        {
            return ApiProblems.BadRequest(ProblemCodes.invalid_level_type, "This unit level cannot host classes.");
        }

        var (effective, _) = await years.GetEffectiveAsync(unit.Path, ct);
        var year = effective.FirstOrDefault(y => y.Id == yearId);
        if (year is null)
        {
            return ApiProblems.BadRequest(ProblemCodes.year_not_effective, "The academic year does not apply to this unit.");
        }

        return year.Status == AcademicYearStatus.Closed ? ApiProblems.Conflict(ProblemCodes.year_closed, "The academic year is closed.") : null;
    }

    internal async Task<IResult?> CheckHomeroomAsync(OrgUnit unit, Guid? teacherUserId, CancellationToken ct) =>
        teacherUserId is { } teacher && !await lookups.IsTeacherAtAsync(unit.OrganizationId, teacher, unit.Path, ct)
            ? ApiProblems.BadRequest(ProblemCodes.teacher_not_in_unit, "The homeroom teacher has no Teacher role at this unit.")
            : null;

    internal async Task<IResult?> ClosedProblemAsync(Class cls, CancellationToken ct)
    {
        if (cls.Status == Class.ClosedStatus)
        {
            return ApiProblems.Conflict(ProblemCodes.class_closed, "A closed class is read-only.");
        }

        var year = (await lookups.YearsAsync(cls.OrganizationId, [cls.AcademicYearId], ct)).GetValueOrDefault(cls.AcademicYearId);
        return year?.Status == AcademicYearStatus.Closed ? ApiProblems.Conflict(ProblemCodes.year_closed, "The academic year is closed.") : null;
    }
}
