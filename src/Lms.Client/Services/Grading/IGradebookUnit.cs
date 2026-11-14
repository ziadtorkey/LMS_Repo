using Lms.Client.Services.Api;

namespace Lms.Client.Services.Grading;

/// <summary>
/// The org unit a class course belongs to (its class's unit): where the gradebook's scales and weight templates are looked up. The gradebook response carries
/// no unit, so the client resolves it from the class course; null when it cannot (the pickers then say so instead of guessing).
/// </summary>
public interface IGradebookUnit
{
    Task<Guid?> UnitOfAsync(Guid classCourseId, CancellationToken ct = default);
}

/// <summary>Resolves the unit through the caller's class courses and the class: enough for a teacher; a supervisor without that course gets null.</summary>
public sealed class GradebookUnit(ILessonPlansApi plans, IClassesApi classes) : IGradebookUnit
{
    public async Task<Guid?> UnitOfAsync(Guid classCourseId, CancellationToken ct = default)
    {
        var mine = await plans.GetMyClassCoursesIfAllowedAsync(ct);
        var course = mine.FirstOrDefault(c => c.Id == classCourseId);
        if (course is null) return null;
        return (await classes.GetAsync(course.ClassId, ct)).OrgUnitId;
    }
}

/// <summary>The demo: the unit the fake scales and templates belong to.</summary>
public sealed class FakeGradebookUnit : IGradebookUnit
{
    public Guid? Unit { get; set; } = FakeGradingScalesApi.DemoUnitId;

    public Task<Guid?> UnitOfAsync(Guid classCourseId, CancellationToken ct = default) => Task.FromResult(Unit);
}
