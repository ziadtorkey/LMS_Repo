using Lms.Shared;
using Lms.Shared.Teaching;

namespace Lms.Client.Services.Api;

/// <summary>Which teachers may teach which courses (FR-TCH-01) and who teaches a class course when (FR-TCH-02).</summary>
public interface ITeachingApi
{
    Task<PagedResult<TeacherEligibilityDto>> ListEligibilityAsync(EligibilityQuery query, CancellationToken ct = default);

    /// <summary>Grants every teacher x every course at the unit (at most 200 pairs); a user without a teacher role there fails with teacher_not_in_unit.</summary>
    Task GrantEligibilityAsync(GrantEligibilityRequest request, CancellationToken ct = default);

    Task RevokeEligibilityAsync(Guid id, CancellationToken ct = default);

    /// <summary>The teacher assignments of a class course: the open ones, or with the ended ones (history) as well.</summary>
    Task<IReadOnlyList<ClassCourseTeacherDto>> ListTeachersAsync(Guid classCourseId, bool includeHistory, CancellationToken ct = default);

    /// <summary>Fails with teacher_not_eligible, teacher_already_assigned or primary_teacher_exists.</summary>
    Task AssignAsync(Guid classCourseId, AssignTeacherRequest request, CancellationToken ct = default);

    Task ReplaceAsync(Guid classCourseId, Guid assignmentId, ReplaceTeacherRequest request, CancellationToken ct = default);
    Task EndAsync(Guid classCourseId, Guid assignmentId, EndTeacherRequest request, CancellationToken ct = default);
    Task<BulkAssignTeacherResultDto> BulkAssignAsync(BulkAssignTeacherRequest request, CancellationToken ct = default);
}

public sealed class TeachingApi(ApiHttp http) : ITeachingApi
{
    public Task<PagedResult<TeacherEligibilityDto>> ListEligibilityAsync(EligibilityQuery q, CancellationToken ct = default)
    {
        var parts = new List<string> { $"page={q.Page}", $"pageSize={q.PageSize}" };
        if (q.OrgUnitId is { } unit) parts.Add($"orgUnitId={unit}");
        if (q.UserId is { } user) parts.Add($"userId={user}");
        if (q.CourseId is { } course) parts.Add($"courseId={course}");
        if (q.IncludeAncestors) parts.Add("includeAncestors=true");
        return http.GetAsync<PagedResult<TeacherEligibilityDto>>($"{Routes.TeacherEligibility}?{string.Join('&', parts)}", ct);
    }

    public Task GrantEligibilityAsync(GrantEligibilityRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.TeacherEligibility, request, ct);

    public Task RevokeEligibilityAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.TeacherEligibilityItem(id), null, ct);

    public async Task<IReadOnlyList<ClassCourseTeacherDto>> ListTeachersAsync(Guid classCourseId, bool includeHistory, CancellationToken ct = default) =>
        await http.GetAsync<List<ClassCourseTeacherDto>>($"{Routes.ClassCourseTeachers(classCourseId)}?history={includeHistory.ToString().ToLowerInvariant()}", ct);

    public Task AssignAsync(Guid classCourseId, AssignTeacherRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.ClassCourseTeachers(classCourseId), request, ct);

    public Task ReplaceAsync(Guid classCourseId, Guid assignmentId, ReplaceTeacherRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.ClassCourseTeacherReplace(classCourseId, assignmentId), request, ct);

    public Task EndAsync(Guid classCourseId, Guid assignmentId, EndTeacherRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.ClassCourseTeacherEnd(classCourseId, assignmentId), request, ct);

    public async Task<BulkAssignTeacherResultDto> BulkAssignAsync(BulkAssignTeacherRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<BulkAssignTeacherResultDto>(HttpMethod.Post, Routes.TeacherAssignmentsBulk, request, ct))!;
}

/// <summary>In-memory fake for the standalone dev server and bUnit tests; keeps the rules (eligibility, one open primary) so conflicts show.</summary>
public sealed class FakeTeachingApi : ITeachingApi
{
    public static readonly Guid Sara = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    public static readonly Guid Omar = Guid.Parse("00000000-0000-0000-0000-0000000000e2");
    public static readonly Guid Noor = Guid.Parse("00000000-0000-0000-0000-0000000000e3");

    private readonly List<TeacherEligibilityDto> _eligibility = [];
    private readonly List<(Guid ClassCourseId, ClassCourseTeacherDto Teacher)> _teachers = [];

    public static IReadOnlyList<(Guid UserId, string NameAr, string NameEn)> Teachers { get; } =
    [
        (Sara, "سارة المعلمة", "Sara Teacher"), (Omar, "عمر المعلم", "Omar Teacher"), (Noor, "نور المعلمة", "Noor Teacher"),
    ];

    public EligibilityQuery? LastQuery { get; private set; }
    public List<GrantEligibilityRequest> Grants { get; } = [];
    public List<Guid> Revoked { get; } = [];
    public List<(Guid ClassCourseId, AssignTeacherRequest Request)> Assigns { get; } = [];
    public List<(Guid ClassCourseId, Guid AssignmentId, ReplaceTeacherRequest Request)> Replaces { get; } = [];
    public List<(Guid ClassCourseId, Guid AssignmentId, EndTeacherRequest Request)> Ends { get; } = [];
    public List<BulkAssignTeacherRequest> Bulks { get; } = [];

    public FakeTeachingApi()
    {
        // Sara and Omar may teach Mathematics, Noor may teach Science, at the school.
        Eligible(Sara, "MATH101"); Eligible(Omar, "MATH101"); Eligible(Noor, "SCI101");
    }

    /// <summary>The fake with a few assignments (the standalone dev server): Sara teaches Grade 1 A Mathematics, Omar assists.</summary>
    public static FakeTeachingApi WithDemoData()
    {
        var api = new FakeTeachingApi();
        var math = FakeCourseIds.For($"{FakeClasses.Grade1A.Code}:MATH101");
        var today = DateOnly.FromDateTime(DateTime.Today);
        api.Seed(math, Sara, TeacherRoles.Primary, today.AddMonths(-2));
        api.Seed(math, Omar, TeacherRoles.Assistant, today.AddMonths(-1));
        return api;
    }

    public IReadOnlyList<TeacherEligibilityDto> Eligibility => _eligibility;

    private void Eligible(Guid userId, string courseCode)
    {
        var teacher = Teachers.FirstOrDefault(t => t.UserId == userId);
        var course = FakeClassCoursesApi.Catalog.First(c => c.Code == courseCode);
        _eligibility.Add(new TeacherEligibilityDto(Guid.NewGuid(), userId, teacher.NameAr ?? "معلم", teacher.NameEn ?? "Teacher", course.Id, course.Code, course.NameAr, course.NameEn,
            FakeUnits.SchoolId, "المدرسة الرئيسية", "Main School"));
    }

    /// <summary>Seeds an assignment of a class course (tests and the dev server).</summary>
    public ClassCourseTeacherDto Seed(Guid classCourseId, Guid userId, string role, DateOnly from, DateOnly? to = null)
    {
        var teacher = Teachers.FirstOrDefault(t => t.UserId == userId);
        var item = new ClassCourseTeacherDto(Guid.NewGuid(), userId, teacher.NameAr ?? "معلم", teacher.NameEn ?? "Teacher", role, from, to, to is null);
        _teachers.Add((classCourseId, item));
        return item;
    }

    public Task<PagedResult<TeacherEligibilityDto>> ListEligibilityAsync(EligibilityQuery query, CancellationToken ct = default)
    {
        LastQuery = query;
        var rows = _eligibility
            .Where(e => query.OrgUnitId is null || AppliesAt(e.OrgUnitId, query.OrgUnitId.Value, query.IncludeAncestors))
            .Where(e => query.UserId is null || e.UserId == query.UserId)
            .Where(e => query.CourseId is null || e.CourseId == query.CourseId)
            .ToList();
        return Task.FromResult(new PagedResult<TeacherEligibilityDto>(rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), rows.Count, query.Page, query.PageSize));
    }

    /// <summary>
    /// The unit filter: by default eligibilities granted at the unit or below; with ancestors, those that apply to a class at the unit
    /// (granted at the unit or any ancestor), the rule teacher assignment enforces.
    /// </summary>
    private static bool AppliesAt(Guid grantedAt, Guid unit, bool includeAncestors)
    {
        var (outer, inner) = includeAncestors ? (grantedAt, unit) : (unit, grantedAt);
        // true when "outer" is "inner" or one of its ancestors
        var cursor = FakeUnits.All.FirstOrDefault(u => u.Id == inner);
        while (cursor is not null)
        {
            if (cursor.Id == outer) return true;
            cursor = cursor.ParentId is { } parent ? FakeUnits.All.FirstOrDefault(u => u.Id == parent) : null;
        }
        return false;
    }

    public Task GrantEligibilityAsync(GrantEligibilityRequest request, CancellationToken ct = default)
    {
        Grants.Add(request);
        foreach (var user in request.UserIds)
            foreach (var courseId in request.CourseIds)
                if (!_eligibility.Any(e => e.UserId == user && e.CourseId == courseId && e.OrgUnitId == request.OrgUnitId)
                    && FakeClassCoursesApi.Catalog.FirstOrDefault(c => c.Id == courseId) is { } course)
                    Eligible(user, course.Code);
        return Task.CompletedTask;
    }

    public Task RevokeEligibilityAsync(Guid id, CancellationToken ct = default)
    {
        Revoked.Add(id);
        _eligibility.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ClassCourseTeacherDto>> ListTeachersAsync(Guid classCourseId, bool includeHistory, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ClassCourseTeacherDto>>(_teachers.Where(t => t.ClassCourseId == classCourseId && (includeHistory || t.Teacher.IsCurrent))
            .Select(t => t.Teacher).OrderBy(t => t.Role).ThenBy(t => t.From).ToList());

    public Task AssignAsync(Guid classCourseId, AssignTeacherRequest request, CancellationToken ct = default)
    {
        Assigns.Add((classCourseId, request));
        if (_teachers.Any(t => t.ClassCourseId == classCourseId && t.Teacher.IsCurrent && t.Teacher.UserId == request.UserId))
            return Task.FromException(new ApiProblemException(409, "teacher_already_assigned", null, null));
        if (request.Role == TeacherRoles.Primary && _teachers.Any(t => t.ClassCourseId == classCourseId && t.Teacher.IsCurrent && t.Teacher.Role == TeacherRoles.Primary))
            return Task.FromException(new ApiProblemException(409, "primary_teacher_exists", null, null));
        Seed(classCourseId, request.UserId, request.Role, request.From ?? DateOnly.FromDateTime(DateTime.Today));
        return Task.CompletedTask;
    }

    public Task ReplaceAsync(Guid classCourseId, Guid assignmentId, ReplaceTeacherRequest request, CancellationToken ct = default)
    {
        Replaces.Add((classCourseId, assignmentId, request));
        var index = _teachers.FindIndex(t => t.Teacher.Id == assignmentId);
        var old = _teachers[index].Teacher;
        _teachers[index] = (classCourseId, old with { To = request.EffectiveOn.AddDays(-1), IsCurrent = false });
        Seed(classCourseId, request.NewUserId, old.Role, request.EffectiveOn);
        return Task.CompletedTask;
    }

    public Task EndAsync(Guid classCourseId, Guid assignmentId, EndTeacherRequest request, CancellationToken ct = default)
    {
        Ends.Add((classCourseId, assignmentId, request));
        var index = _teachers.FindIndex(t => t.Teacher.Id == assignmentId);
        _teachers[index] = (classCourseId, _teachers[index].Teacher with { To = request.LastDay, IsCurrent = false });
        return Task.CompletedTask;
    }

    public Task<BulkAssignTeacherResultDto> BulkAssignAsync(BulkAssignTeacherRequest request, CancellationToken ct = default)
    {
        Bulks.Add(request);
        int assigned = 0, skipped = 0;
        foreach (var id in request.ClassCourseIds)
        {
            if (_teachers.Any(t => t.ClassCourseId == id && t.Teacher.IsCurrent && t.Teacher.UserId == request.UserId)) { skipped++; continue; }
            Seed(id, request.UserId, request.Role, DateOnly.FromDateTime(DateTime.Today));
            assigned++;
        }
        return Task.FromResult(new BulkAssignTeacherResultDto(assigned, skipped));
    }
}
