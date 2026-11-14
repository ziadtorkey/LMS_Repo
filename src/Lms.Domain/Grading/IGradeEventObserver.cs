using Lms.Domain.Classes;

namespace Lms.Domain.Grading;

/// <summary>
/// Called by the grade event writer in the same unit of work, right after a new <see cref="GradeEvent"/> was added (before the caller saves),
/// so what an observer adds (the gradebook item of the source) is committed or rolled back together with the grade itself (M5 L4: no writes on
/// read). An observer must be idempotent and must not save.
/// </summary>
public interface IGradeEventObserver
{
    Task OnRecordedAsync(GradeEvent gradeEvent, ClassCourse classCourse, CancellationToken ct);
}
