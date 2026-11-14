using Lms.Client.Services.Api;
using Lms.Shared.AcademicYears;

namespace Lms.Client.Services.Grading;

/// <summary>
/// The org unit a class course belongs to (its class's unit): where the gradebook's scales and weight templates are looked up. The gradebook response carries
/// no unit, so the client resolves it from the class course; null when it cannot (the pickers then say so instead of guessing).
/// </summary>
public interface IGradebookUnit
{
    Task<Guid?> UnitOfAsync(Guid classCourseId, CancellationToken ct = default);

    /// <summary>The terms of the class's academic year in order (two or three per year, D62), for the term picker; empty when they cannot be resolved.</summary>
    Task<IReadOnlyList<TermDto>> TermsAsync(Guid classCourseId, CancellationToken ct = default);
}

/// <summary>Resolves the unit through the caller's class courses and the class: enough for a teacher; a supervisor without that course gets null.</summary>
public sealed class GradebookUnit(ILessonPlansApi plans, IClassesApi classes, IYearsApi years) : IGradebookUnit
{
    private async Task<Lms.Shared.Classes.ClassSummaryDto?> ClassOfAsync(Guid classCourseId, CancellationToken ct)
    {
        var mine = await plans.GetMyClassCoursesIfAllowedAsync(ct);
        var course = mine.FirstOrDefault(c => c.Id == classCourseId);
        return course is null ? null : await classes.GetAsync(course.ClassId, ct);
    }

    public async Task<Guid?> UnitOfAsync(Guid classCourseId, CancellationToken ct = default) => (await ClassOfAsync(classCourseId, ct))?.OrgUnitId;

    public async Task<IReadOnlyList<TermDto>> TermsAsync(Guid classCourseId, CancellationToken ct = default)
    {
        if (await ClassOfAsync(classCourseId, ct) is not { } cls) return [];
        var context = await years.GetEffectiveAsync(cls.OrgUnitId, ct);
        return [.. (context.Years.FirstOrDefault(y => y.Id == cls.AcademicYearId)?.Terms ?? []).OrderBy(t => t.Order)];
    }
}

/// <summary>The demo: the unit the fake scales and templates belong to.</summary>
public sealed class FakeGradebookUnit : IGradebookUnit
{
    public Guid? Unit { get; set; } = FakeGradingScalesApi.DemoUnitId;

    /// <summary>The demo year's two terms; the first is the one the demo gradebook is for.</summary>
    public List<TermDto> Terms { get; } =
    [
        new(FakeGradebookApi.DemoTermId, "الفصل الأول", "Term 1", 1, new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 20), true),
        new(FakeGradebookApi.Term2Id, "الفصل الثاني", "Term 2", 2, new DateOnly(2027, 2, 1), new DateOnly(2027, 6, 20), false),
    ];

    public Task<IReadOnlyList<TermDto>> TermsAsync(Guid classCourseId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TermDto>>(Terms);

    public Task<Guid?> UnitOfAsync(Guid classCourseId, CancellationToken ct = default) => Task.FromResult(Unit);
}
