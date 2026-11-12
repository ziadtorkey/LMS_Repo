using Lms.Shared;
using Lms.Shared.Courses;
using Lms.Shared.People;

namespace Lms.Client.Services.Api;

/// <summary>In-memory courses for the standalone client and bUnit tests; records what the UI sent.</summary>
public sealed class FakeCoursesApi : ICoursesApi
{
    public static readonly Guid MeUserId = Guid.Parse("00000000-0000-0000-0000-0000000000e1");

    private readonly List<CourseDetailDto> _courses = [];
    private readonly List<CourseAllocationDto> _allocations = [];

    public CourseQuery? LastQuery { get; private set; }
    public SaveCourseRequest? LastSave { get; private set; }
    public Guid? LastUpdatedId { get; private set; }
    public AllocateCoursesRequest? LastAllocate { get; private set; }
    public SetCourseRestrictionsRequest? LastRestrictions { get; private set; }
    public SetCourseSupervisorsRequest? LastSupervisors { get; private set; }
    public List<Guid> RemovedAllocations { get; } = [];

    /// <summary>How many active enrollments the next restriction save reports as no longer passing.</summary>
    public int AffectedEnrollments { get; set; }

    /// <summary>When set, the next allocation change throws it (e.g. allocation_in_use).</summary>
    public Exception? AllocationFailure { get; set; }

    /// <summary>When set, the next create or update throws it (tests simulate server problems).</summary>
    public Exception? WriteFailure { get; set; }

    public IReadOnlyList<CourseDetailDto> All => _courses;

    public FakeCoursesApi()
    {
        var math = Add("MATH101", "الرياضيات", "Mathematics", FakeUnits.SchoolId, "المدرسة الرئيسية", "Main School", 5, "en", 0);
        AddAllocation(math.Summary, FakeUnits.SchoolId);
        AddAllocation(math.Summary, FakeUnits.GradeId);
        Replace(math.Summary.Id, c => c with
        {
            Restrictions = [new CourseRestrictionDto(CourseRestrictionTypes.Religion, [Religions.Muslim])],
            Supervisors = [new CourseSupervisorDto(MeUserId, "المشرف", "The Supervisor", "sup@x.test")],
        });
        Add("SCI101", "العلوم", "Science", FakeUnits.SchoolId, "المدرسة الرئيسية", "Main School", 4, "en", 0, CourseStatuses.Inactive);
        // Owned at an ancestor: visible here, read-only (D37).
        var arb = Add("ARB101", "اللغة العربية", "Arabic", FakeUnits.GroupId, "مدارس النور", "Al Noor Schools", 6, "ar", 0, inherited: true);
        AddAllocation(arb.Summary, FakeUnits.GroupId);
    }

    public CourseDetailDto Add(string code, string nameAr, string nameEn, Guid ownerId, string ownerAr, string ownerEn,
        int periods, string language, int allocations, string status = CourseStatuses.Active, bool inherited = false)
    {
        var summary = new CourseSummaryDto(Guid.NewGuid(), code, nameAr, nameEn, language, periods, ownerId, ownerAr, ownerEn,
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

    private void Replace(Guid id, Func<CourseDetailDto, CourseDetailDto> change)
    {
        var i = _courses.FindIndex(c => c.Summary.Id == id);
        _courses[i] = change(_courses[i]);
    }

    private CourseAllocationDto AddAllocation(CourseSummaryDto course, Guid unitId)
    {
        var unit = FakeUnits.All.First(u => u.Id == unitId);
        var row = new CourseAllocationDto(Guid.NewGuid(), course.Id, course.Code, course.NameAr, course.NameEn, unitId, unit.NameAr, unit.NameEn, false, 0);
        _allocations.Add(row);
        RecountAllocations(course.Id);
        return row;
    }

    private void RecountAllocations(Guid courseId) =>
        Replace(courseId, c => c with { Summary = c.Summary with { AllocationCount = _allocations.Count(a => a.CourseId == courseId) } });

    private static IEnumerable<Guid> AncestorsOrSelf(Guid unitId)
    {
        for (Guid? id = unitId; id is { } u; id = FakeUnits.All.FirstOrDefault(x => x.Id == u)?.ParentId) yield return u;   // a unit the fake does not know has no ancestors
    }

    public Task<UnitCoursesDto> GetUnitCoursesAsync(Guid orgUnitId, CancellationToken ct = default)
    {
        var chain = AncestorsOrSelf(orgUnitId).ToHashSet();
        var available = _allocations.Where(a => chain.Contains(a.OrgUnitId)).Select(a => a with { IsInherited = a.OrgUnitId != orgUnitId }).ToList();
        var below = _allocations.Where(a => a.OrgUnitId != orgUnitId && AncestorsOrSelf(a.OrgUnitId).Contains(orgUnitId)).ToList();
        return Task.FromResult(new UnitCoursesDto(available, below));
    }

    public Task AllocateAsync(AllocateCoursesRequest request, CancellationToken ct = default)
    {
        if (AllocationFailure is not null) return Task.FromException(AllocationFailure);
        LastAllocate = request;
        foreach (var courseId in request.CourseIds)
        {
            if (_allocations.Any(a => a.CourseId == courseId && a.OrgUnitId == request.OrgUnitId))
                return Task.FromException(new ApiProblemException(409, "allocation_exists", null, null));
            AddAllocation(_courses.First(c => c.Summary.Id == courseId).Summary, request.OrgUnitId);
        }
        return Task.CompletedTask;
    }

    public Task RemoveAllocationAsync(Guid allocationId, CancellationToken ct = default)
    {
        if (AllocationFailure is not null) return Task.FromException(AllocationFailure);
        RemovedAllocations.Add(allocationId);
        var row = _allocations.FirstOrDefault(a => a.Id == allocationId);
        if (row is null) return Task.FromException(new ApiProblemException(404, "not_found", null, null));
        _allocations.Remove(row);
        RecountAllocations(row.CourseId);
        return Task.CompletedTask;
    }

    public Task<RestrictionsSavedResult> SetRestrictionsAsync(Guid id, SetCourseRestrictionsRequest request, CancellationToken ct = default)
    {
        if (WriteFailure is not null) return Task.FromException<RestrictionsSavedResult>(WriteFailure);
        LastRestrictions = request;
        Replace(id, c => c with { Restrictions = request.Restrictions.Select(r => new CourseRestrictionDto(r.Type, r.Values)).ToList() });
        return Task.FromResult(new RestrictionsSavedResult(AffectedEnrollments));
    }

    public Task SetSupervisorsAsync(Guid id, SetCourseSupervisorsRequest request, CancellationToken ct = default)
    {
        if (WriteFailure is not null) return Task.FromException(WriteFailure);
        LastSupervisors = request;
        Replace(id, c => c with
        {
            Supervisors = request.UserIds.Select(u => c.Supervisors.FirstOrDefault(s => s.UserId == u) ?? new CourseSupervisorDto(u, "مشرف", "Supervisor", null)).ToList(),
        });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CourseSummaryDto>> GetMySupervisedAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CourseSummaryDto>>(_courses.Where(c => c.Supervisors.Any(s => s.UserId == MeUserId)).Select(c => c.Summary).ToList());

    /// <summary>The course's current allocations (the profile's Allocations tab reads them through the owner unit).</summary>
    public IReadOnlyList<CourseAllocationDto> AllocationsOf(Guid courseId) => _allocations.Where(a => a.CourseId == courseId).ToList();

    private bool CodeTaken(string code, Guid? except) =>
        _courses.Any(c => c.Summary.Id != except && string.Equals(c.Summary.Code, code, StringComparison.OrdinalIgnoreCase));
}
