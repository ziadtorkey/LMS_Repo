using Lms.Shared;
using Lms.Shared.Courses;

namespace Lms.Client.Services.Api;

/// <summary>In-memory courses for the standalone client and bUnit tests; records what the UI sent.</summary>
public sealed class FakeCoursesApi : ICoursesApi
{
    private readonly List<CourseDetailDto> _courses = [];

    public CourseQuery? LastQuery { get; private set; }
    public SaveCourseRequest? LastSave { get; private set; }
    public Guid? LastUpdatedId { get; private set; }

    /// <summary>When set, the next create or update throws it (tests simulate server problems).</summary>
    public Exception? WriteFailure { get; set; }

    public IReadOnlyList<CourseDetailDto> All => _courses;

    public FakeCoursesApi()
    {
        Add("MATH101", "الرياضيات", "Mathematics", FakeUnits.SchoolId, "المدرسة الرئيسية", "Main School", 5, "en", 2);
        Add("SCI101", "العلوم", "Science", FakeUnits.SchoolId, "المدرسة الرئيسية", "Main School", 4, "en", 0, CourseStatuses.Inactive);
        // Owned at an ancestor: visible here, read-only (D37).
        Add("ARB101", "اللغة العربية", "Arabic", FakeUnits.GroupId, "مدارس النور", "Al Noor Schools", 6, "ar", 3, inherited: true);
    }

    public CourseDetailDto Add(string code, string nameAr, string nameEn, Guid ownerId, string ownerAr, string ownerEn,
        int periods, string language, int allocations, string status = CourseStatuses.Active, bool inherited = false)
    {
        var summary = new CourseSummaryDto(FakeCourseIds.For(code), code, nameAr, nameEn, language, periods, ownerId, ownerAr, ownerEn,
            status, inherited, allocations, false);
        var detail = new CourseDetailDto(summary, null, null, [], [], !inherited);
        _courses.Add(detail);
        return detail;
    }

    public Task<PagedResult<CourseSummaryDto>> ListAsync(CourseQuery query, CancellationToken ct = default)
    {
        LastQuery = query;
        var rows = _courses.Select(c => c.Summary)
            .Where(c => query.IncludeInherited || !c.IsInherited)
            .Where(c => query.Status is null || c.Status == query.Status)
            .Where(c => string.IsNullOrWhiteSpace(query.Search)
                || c.Code.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || c.NameEn.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || c.NameAr.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult(new PagedResult<CourseSummaryDto>(
            rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), rows.Count, query.Page, query.PageSize));
    }

    public Task<CourseDetailDto> GetAsync(Guid id, CancellationToken ct = default) =>
        _courses.FirstOrDefault(c => c.Summary.Id == id) is { } found
            ? Task.FromResult(found)
            : Task.FromException<CourseDetailDto>(new ApiProblemException(404, "not_found", null, null));

    public Task<CourseDetailDto> CreateAsync(SaveCourseRequest request, CancellationToken ct = default)
    {
        if (WriteFailure is not null) return Task.FromException<CourseDetailDto>(WriteFailure);
        LastSave = request;
        if (CodeTaken(request.Code, null))
            return Task.FromException<CourseDetailDto>(new ApiProblemException(409, "course_code_taken", null, null, "code"));
        var created = Add(request.Code.ToUpperInvariant(), request.NameAr, request.NameEn, request.OwnerOrgUnitId, "", "",
            request.WeeklyPeriods, request.Language, request.AllocateToOrgUnitIds?.Count ?? 0);
        return Task.FromResult(created with { DescriptionAr = request.DescriptionAr, DescriptionEn = request.DescriptionEn });
    }

    public Task UpdateAsync(Guid id, SaveCourseRequest request, CancellationToken ct = default)
    {
        if (WriteFailure is not null) return Task.FromException(WriteFailure);
        LastSave = request;
        LastUpdatedId = id;
        if (CodeTaken(request.Code, id))
            return Task.FromException(new ApiProblemException(409, "course_code_taken", null, null, "code"));
        var index = _courses.FindIndex(c => c.Summary.Id == id);
        if (index < 0) return Task.FromException(new ApiProblemException(404, "not_found", null, null));
        var old = _courses[index];
        _courses[index] = old with
        {
            Summary = old.Summary with
            {
                Code = request.Code.ToUpperInvariant(), NameAr = request.NameAr, NameEn = request.NameEn,
                Language = request.Language, WeeklyPeriods = request.WeeklyPeriods, OwnerOrgUnitId = request.OwnerOrgUnitId,
            },
            DescriptionAr = request.DescriptionAr,
            DescriptionEn = request.DescriptionEn,
        };
        return Task.CompletedTask;
    }

    private bool CodeTaken(string code, Guid? except) =>
        _courses.Any(c => c.Summary.Id != except && string.Equals(c.Summary.Code, code, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Stable ids of the fake courses by code, so the fake class courses and the fake catalog agree on which course is which.</summary>
public static class FakeCourseIds
{
    public static Guid For(string code)
    {
        var bytes = new byte[16];
        System.Text.Encoding.UTF8.GetBytes(code, bytes.AsSpan(0, Math.Min(code.Length, 16)));
        return new Guid(bytes);
    }
}
