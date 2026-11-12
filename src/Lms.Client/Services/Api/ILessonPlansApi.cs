using Lms.Shared;
using Lms.Shared.Classes;

namespace Lms.Client.Services.Api;

/// <summary>"My class courses" and the class lesson plan tree (FR-LP-05, FR-TCH-03). Plans are read-only here.</summary>
public interface ILessonPlansApi
{
    /// <summary>Class courses of the caller: taught (teacher) or enrolled in (student).</summary>
    Task<IReadOnlyList<ClassCourseDto>> GetMyClassCoursesAsync(CancellationToken ct = default);

    /// <summary>
    /// The same read for a page that only decorates itself with it (Home cards): a caller without LessonPlans.View gets an empty
    /// list, not the Forbidden page.
    /// </summary>
    async Task<IReadOnlyList<ClassCourseDto>> GetMyClassCoursesIfAllowedAsync(CancellationToken ct = default)
    {
        try { return await GetMyClassCoursesAsync(ct); }
        catch (ApiForbiddenException) { return []; }
    }

    Task<ClassLessonPlanDto> GetPlanAsync(Guid classCourseId, CancellationToken ct = default);
}

public sealed class LessonPlansApi(ApiHttp http) : ILessonPlansApi
{
    public Task<IReadOnlyList<ClassCourseDto>> GetMyClassCoursesAsync(CancellationToken ct = default) =>
        http.GetAsync<IReadOnlyList<ClassCourseDto>>(Routes.MyClassCourses, ct);

    // Optional read: a plain 403 (no LessonPlans.View) must not send the user to the Forbidden page.
    public async Task<IReadOnlyList<ClassCourseDto>> GetMyClassCoursesIfAllowedAsync(CancellationToken ct = default)
    {
        try { return await http.GetOptionalAsync<List<ClassCourseDto>>(Routes.MyClassCourses, ct); }
        catch (ApiForbiddenException) { return []; }
    }

    public Task<ClassLessonPlanDto> GetPlanAsync(Guid classCourseId, CancellationToken ct = default) =>
        http.GetAsync<ClassLessonPlanDto>(Routes.ClassCourseLessonPlan(classCourseId), ct);
}

/// <summary>
/// In-memory lessons plans for the standalone dev server and tests. <see cref="HideHiddenItems"/> mimics the student view
/// (the server leaves hidden sections and lessons out for the Student role).
/// </summary>
public sealed class FakeLessonPlansApi : ILessonPlansApi
{
    public static readonly Guid ArabicClassCourseId = Guid.Parse("00000000-0000-0000-0000-00000000cc01");
    public static readonly Guid ScienceClassCourseId = Guid.Parse("00000000-0000-0000-0000-00000000cc02");
    public static readonly Guid EmptyClassCourseId = Guid.Parse("00000000-0000-0000-0000-00000000cc03");

    private static readonly Guid Grade1 = Guid.Parse("00000000-0000-0000-0000-00000000c101");
    private static readonly Guid Grade2 = Guid.Parse("00000000-0000-0000-0000-00000000c102");

    public bool HideHiddenItems { get; set; }

    public List<ClassCourseDto> Mine { get; } =
    [
        new(ArabicClassCourseId, Grade1, "الصف الأول أ", "Grade 1 A", Guid.Parse("00000000-0000-0000-0000-00000000c001"), "ARB101", "اللغة العربية", "Arabic",
            false, 12, [], 3),
        new(ScienceClassCourseId, Grade2, "الصف الثاني ب", "Grade 2 B", Guid.Parse("00000000-0000-0000-0000-00000000c002"), "SCI201", "العلوم", "Science",
            true, 9, [], 1),
        new(EmptyClassCourseId, Grade2, "الصف الثاني ب", "Grade 2 B", Guid.Parse("00000000-0000-0000-0000-00000000c003"), "ART201", "التربية الفنية", "Art",
            false, 9, [], null),
    ];

    public Dictionary<Guid, ClassLessonPlanDto> Plans { get; } = new()
    {
        [ArabicClassCourseId] = new(ArabicClassCourseId, 3, new DateTime(2026, 9, 20, 8, 30, 0, DateTimeKind.Utc), "Chapter",
        [
            new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), 1, "القراءة", "Reading", false,
            [
                new(Guid.Parse("00000000-0000-0000-0000-0000000000b1"), 1, "حروف الهجاء", "The alphabet", "التعرف على الحروف", "Recognise the letters", new DateOnly(2026, 9, 6), false),
                new(Guid.Parse("00000000-0000-0000-0000-0000000000b2"), 2, "الكلمات الأولى", "First words", null, null, null, false),
                new(Guid.Parse("00000000-0000-0000-0000-0000000000b3"), 3, "درس قديم", "Retired lesson", null, null, new DateOnly(2026, 9, 20), true),
            ]),
            new(Guid.Parse("00000000-0000-0000-0000-0000000000a2"), 2, "الكتابة", "Writing", false,
            [
                new(Guid.Parse("00000000-0000-0000-0000-0000000000b4"), 1, "الخط", "Handwriting", null, null, new DateOnly(2026, 10, 4), false),
            ]),
            new(Guid.Parse("00000000-0000-0000-0000-0000000000a3"), 3, "فصل ملغى", "Retired chapter", true,
            [
                new(Guid.Parse("00000000-0000-0000-0000-0000000000b5"), 1, "درس ملغى", "Retired chapter lesson", null, null, null, true),
            ]),
        ]),
        [ScienceClassCourseId] = new(ScienceClassCourseId, 1, new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), "Unit",
        [
            new(Guid.Parse("00000000-0000-0000-0000-0000000000a4"), 1, "المادة", "Matter", false,
            [
                new(Guid.Parse("00000000-0000-0000-0000-0000000000b6"), 1, "المواد الصلبة والسائلة", "Solids and liquids", "التمييز بين الحالات", "Tell the states apart", new DateOnly(2026, 9, 13), false),
            ]),
        ]),
        [EmptyClassCourseId] = new(EmptyClassCourseId, null, null, "Chapter", []),
    };

    public Task<IReadOnlyList<ClassCourseDto>> GetMyClassCoursesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ClassCourseDto>>(Mine.ToList());

    public Task<ClassLessonPlanDto> GetPlanAsync(Guid classCourseId, CancellationToken ct = default)
    {
        if (!Plans.TryGetValue(classCourseId, out var plan))
            return Task.FromException<ClassLessonPlanDto>(new ApiProblemException(404, "not_found", null, null));
        if (!HideHiddenItems) return Task.FromResult(plan);
        var visible = plan.Sections.Where(s => !s.IsHidden)
            .Select(s => s with { Lessons = s.Lessons.Where(l => !l.IsHidden).ToList() }).ToList();
        return Task.FromResult(plan with { Sections = visible });
    }
}
