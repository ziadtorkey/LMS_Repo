using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Students;

/// <summary>GET /api/students and GET /api/students/{id}: the unit scope and the caller's relationship rule apply to both (D32).</summary>
public sealed class StudentQueries(LmsDbContext db, ICurrentContext context, IRelationshipScope relationships, StudentMapper mapper)
{
    public async Task<IResult> ListAsync(StudentQuery query, HttpContext http, CancellationToken ct)
    {
        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > Paging.MaxPageSize)
        {
            return StudentValidators.Problem(new Dictionary<string, string[]> { [query.Page < 1 ? "Page" : "PageSize"] = ["paging_invalid"] });
        }

        if ((query.Status is not null && !PersonStatuses.All.Contains(query.Status))
            || (query.Gender is not null && !Genders.All.Contains(query.Gender))
            || (query.Sort is not null && !StudentSorts.All.Contains(query.Sort)))
        {
            return StudentValidators.Problem(new Dictionary<string, string[]> { ["Filter"] = ["invalid_value"] });
        }

        var scope = await ScopeUnitAsync(query.OrgUnitId, http, ct);
        if (scope.Problem is not null)
        {
            return scope.Problem;
        }

        var unit = scope.Unit!;
        var students = db.Set<StudentProfile>()
            .Where(s => query.IncludeDescendants ? s.OrgUnitPath.StartsWith(unit.Path) : s.OrgUnitId == unit.Id);

        // D32/D42: a teacher reaches only the students of their own class courses (active enrollments) and homeroom classes.
        var filter = await relationships.ForAsync(Permissions.Students.View, ct);
        students = filter.Students(students, db);

        if (query.Status is not null)
        {
            var status = Enum.Parse<PersonStatus>(query.Status);
            students = students.Where(s => s.Status == status);
        }

        if (query.Gender is not null)
        {
            students = students.Where(s => s.Gender == query.Gender);
        }

        if (query.ClassId is { } classId)
        {
            students = students.Where(s => db.Set<ClassStudent>().Any(c => c.StudentId == s.Id && c.ClassId == classId && c.Status == ClassStudent.ActiveStatus));
        }

        if (query.Unplaced)
        {
            var year = query.AcademicYearId;
            students = students.Where(s => !db.Set<ClassStudent>().Any(c =>
                c.StudentId == s.Id && c.Status == ClassStudent.ActiveStatus && (year == null || c.AcademicYearId == year)));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var text = query.Search.Trim();
            var key = PersonNormalizer.NameKey(text);
            var nationalId = PersonNormalizer.NationalId(text);
            var upper = text.ToUpperInvariant();
            students = students.Where(s =>
                s.StudentCode.StartsWith(upper) || (nationalId != null && s.NationalId == nationalId)
                || (key != string.Empty && (s.NameArKey.Contains(key) || s.NameEnKey.Contains(key))));
        }

        var ordered = query.Sort switch
        {
            StudentSorts.NameDesc => students.OrderByDescending(s => s.NameEnKey).ThenBy(s => s.Id),
            StudentSorts.Code => students.OrderBy(s => s.StudentCode).ThenBy(s => s.Id),
            StudentSorts.CodeDesc => students.OrderByDescending(s => s.StudentCode).ThenBy(s => s.Id),
            _ => students.OrderBy(s => s.NameEnKey).ThenBy(s => s.Id),
        };
        var total = await ordered.CountAsync(ct);
        var rows = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Results.Ok(new PagedResult<StudentSummaryDto>(await mapper.SummariesAsync(rows, query.AcademicYearId, ct), total, query.Page, query.PageSize));
    }

    public async Task<IResult> GetAsync(Guid id, CancellationToken ct)
    {
        var student = await FindVisibleAsync(id, ct);
        return student is null ? ApiProblems.NotFound() : Results.Ok(await mapper.DetailAsync(student, ct));
    }

    /// <summary>The student inside the caller's unit scope and relationship filter; anything else does not exist for the caller.</summary>
    public async Task<StudentProfile?> FindVisibleAsync(Guid id, CancellationToken ct)
    {
        var student = context.OrganizationId is null ? null : await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (student is null)
        {
            return null;
        }

        var filter = await relationships.ForAsync(Permissions.Students.View, ct);
        return filter.IsUnrestricted || await filter.Students(db.Set<StudentProfile>().Where(s => s.Id == id), db).AnyAsync(ct) ? student : null;
    }

    private async Task<UnitAccessResult> ScopeUnitAsync(Guid? orgUnitId, HttpContext http, CancellationToken ct)
    {
        if (orgUnitId is { } given)
        {
            return await http.CheckUnitAsync(Permissions.Students.View, given, ct);
        }

        var scopePath = context.ScopePath;
        var unit = scopePath is null ? null : await db.Set<OrgUnit>().FirstOrDefaultAsync(u => u.Path == scopePath, ct);
        return unit is null
            ? new UnitAccessResult(null, ApiProblems.NotFound())
            : await http.CheckUnitAsync(Permissions.Students.View, unit.Id, ct);
    }
}
