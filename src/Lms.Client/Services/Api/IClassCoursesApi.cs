using Lms.Shared;
using Lms.Shared.Classes;
using Lms.Shared.Courses;
using Lms.Shared.Teaching;

namespace Lms.Client.Services.Api;

/// <summary>Courses taught in classes: a class's courses, the optional flag, removal, bulk assignment and the distribution matrix (FR-CLS-03, FR-CLS-05).</summary>
public interface IClassCoursesApi
{
    Task<IReadOnlyList<ClassCourseDto>> ListForClassAsync(Guid classId, CancellationToken ct = default);

    /// <summary>Adds courses available at the class's unit; a course that is not available fails with course_not_available.</summary>
    Task AddAsync(Guid classId, AddClassCoursesRequest request, CancellationToken ct = default);

    Task UpdateAsync(Guid classCourseId, UpdateClassCourseRequest request, CancellationToken ct = default);

    /// <summary>Fails with class_course_in_use while students are enrolled or a lesson plan has progress.</summary>
    Task RemoveAsync(Guid classCourseId, CancellationToken ct = default);

    /// <summary>Adds the course to every live class at the unit and below; classes that already have it are skipped.</summary>
    Task<BulkAssignResultDto> BulkAssignAsync(BulkAssignCourseRequest request, CancellationToken ct = default);

    Task<DistributionMatrixDto> GetMatrixAsync(Guid orgUnitId, Guid academicYearId, CancellationToken ct = default);
}

public sealed class ClassCoursesApi(ApiHttp http) : IClassCoursesApi
{
    public async Task<IReadOnlyList<ClassCourseDto>> ListForClassAsync(Guid classId, CancellationToken ct = default) =>
        await http.GetAsync<List<ClassCourseDto>>(Routes.ClassCoursesOfClass(classId), ct);

    public Task AddAsync(Guid classId, AddClassCoursesRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.ClassCoursesOfClass(classId), request, ct);

    public Task UpdateAsync(Guid classCourseId, UpdateClassCourseRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.ClassCourse(classCourseId), request, ct);

    public Task RemoveAsync(Guid classCourseId, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.ClassCourse(classCourseId), null, ct);

    public async Task<BulkAssignResultDto> BulkAssignAsync(BulkAssignCourseRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<BulkAssignResultDto>(HttpMethod.Post, Routes.ClassCoursesBulkAssign, request, ct))!;

    public Task<DistributionMatrixDto> GetMatrixAsync(Guid orgUnitId, Guid academicYearId, CancellationToken ct = default) =>
        http.GetAsync<DistributionMatrixDto>(Routes.ClassesMatrixFor(orgUnitId, academicYearId), ct);
}

/// <summary>In-memory fake for the standalone dev server and bUnit tests; records what the UI sent.</summary>
public sealed class FakeClassCoursesApi : IClassCoursesApi
{
    private readonly List<ClassCourseDto> _classCourses = [];

    public List<(Guid ClassId, AddClassCoursesRequest Request)> Adds { get; } = [];
    public List<(Guid Id, UpdateClassCourseRequest Request)> Updates { get; } = [];
    public List<Guid> Removed { get; } = [];
    public List<BulkAssignCourseRequest> BulkAssigns { get; } = [];

    /// <summary>The courses of the fake catalog by code, as the matrix columns and the add dialog know them.</summary>
    public static IReadOnlyList<CourseSummaryDto> Catalog { get; } =
    [
        Course("MATH101", "الرياضيات", "Mathematics"),
        Course("SCI101", "العلوم", "Science"),
        Course("ARB101", "اللغة العربية", "Arabic"),
    ];

    /// <summary>When set, the next add / remove / bulk assign throws it (tests simulate class_course_in_use and the like).</summary>
    public Exception? Failure { get; set; }

    public IReadOnlyList<ClassCourseDto> All => _classCourses;

    public FakeClassCoursesApi()
    {
        // Grade 1 A teaches Mathematics (required) and Science (optional); Grade 1 B only Mathematics.
        Add(FakeClasses.Grade1A, Catalog[0], false, 12);
        Add(FakeClasses.Grade1A, Catalog[1], true, 3);
        Add(FakeClasses.Grade1B, Catalog[0], false, 2);
    }

    private static CourseSummaryDto Course(string code, string ar, string en) =>
        new(FakeCourseIds.For(code), code, ar, en, "en", 4, FakeUnits.SchoolId, "المدرسة الرئيسية", "Main School", CourseStatuses.Active, false, 1, false);

    public ClassCourseDto Add(ClassSummaryDto cls, CourseSummaryDto course, bool optional, int enrolled)
    {
        var item = new ClassCourseDto(Guid.NewGuid(), cls.Id, cls.NameAr, cls.NameEn, course.Id, course.Code, course.NameAr, course.NameEn, optional, enrolled, [], null);
        _classCourses.Add(item);
        return item;
    }

    /// <summary>Sets the current teachers shown for a class course (tests).</summary>
    public void ReplaceTeachers(Guid classCourseId, IReadOnlyList<ClassCourseTeacherDto> teachers)
    {
        var index = _classCourses.FindIndex(c => c.Id == classCourseId);
        _classCourses[index] = _classCourses[index] with { CurrentTeachers = teachers };
    }

    private void FailIfSet()
    {
        if (Failure is not { } failure) return;
        Failure = null;
        throw failure;
    }

    public Task<IReadOnlyList<ClassCourseDto>> ListForClassAsync(Guid classId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ClassCourseDto>>(_classCourses.Where(c => c.ClassId == classId).ToList());

    public Task AddAsync(Guid classId, AddClassCoursesRequest request, CancellationToken ct = default)
    {
        Adds.Add((classId, request));
        FailIfSet();
        var cls = FakeClasses.All.First(c => c.Id == classId);
        foreach (var courseId in request.CourseIds)
            if (Catalog.FirstOrDefault(c => c.Id == courseId) is { } course && !_classCourses.Any(c => c.ClassId == classId && c.CourseId == courseId))
                Add(cls, course, request.IsOptional, 0);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Guid classCourseId, UpdateClassCourseRequest request, CancellationToken ct = default)
    {
        Updates.Add((classCourseId, request));
        var index = _classCourses.FindIndex(c => c.Id == classCourseId);
        if (index >= 0) _classCourses[index] = _classCourses[index] with { IsOptional = request.IsOptional };
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid classCourseId, CancellationToken ct = default)
    {
        Removed.Add(classCourseId);
        FailIfSet();
        _classCourses.RemoveAll(c => c.Id == classCourseId);
        return Task.CompletedTask;
    }

    public Task<BulkAssignResultDto> BulkAssignAsync(BulkAssignCourseRequest request, CancellationToken ct = default)
    {
        BulkAssigns.Add(request);
        FailIfSet();
        var course = Catalog.First(c => c.Id == request.CourseId);
        int added = 0, skipped = 0;
        foreach (var cls in FakeClasses.All.Where(c => c.OrgUnitId == request.OrgUnitId))
        {
            if (_classCourses.Any(c => c.ClassId == cls.Id && c.CourseId == course.Id)) { skipped++; continue; }
            Add(cls, course, request.IsOptional, 0);
            added++;
        }
        return Task.FromResult(new BulkAssignResultDto(added, skipped));
    }

    public Task<DistributionMatrixDto> GetMatrixAsync(Guid orgUnitId, Guid academicYearId, CancellationToken ct = default)
    {
        var classes = FakeClasses.All.Where(c => c.OrgUnitId == orgUnitId).ToList();
        var cells = _classCourses.Where(c => classes.Any(k => k.Id == c.ClassId))
            .Select(c => new MatrixCellDto(c.ClassId, c.CourseId, c.Id, c.IsOptional)).ToList();
        return Task.FromResult(new DistributionMatrixDto(classes, Catalog, cells));
    }
}
