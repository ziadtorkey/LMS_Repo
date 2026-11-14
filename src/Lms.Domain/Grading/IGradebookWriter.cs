using Lms.Domain.Classes;

namespace Lms.Domain.Grading;

/// <summary>
/// The only write path into a gradebook (M5 Task 7): it refuses a locked gradebook (409 <c>gradebook_locked</c>), a closed class or year
/// (the placement guard) and a stale row version (409 <c>concurrency_conflict</c>). Methods add to the current unit of work and never save;
/// the caller commits.
/// </summary>
public interface IGradebookWriter
{
    /// <summary>The gradebook of the class course and term, created with the client defaults and its Uncategorised category when missing.</summary>
    Task<Gradebook> EnsureGradebookAsync(ClassCourse classCourse, Guid termId, CancellationToken ct);

    /// <summary>The item of a grade event source (created hidden in Uncategorised when missing). Idempotent.</summary>
    Task<GradeItem> EnsureSourceItemAsync(
        Gradebook gradebook, int sourceKind, Guid sourceId, string titleAr, string titleEn, decimal maxPoints, CancellationToken ct);

    /// <summary>A manual item (paper test), hidden until published.</summary>
    Task<GradeItem> AddManualItemAsync(
        Gradebook gradebook, Guid categoryId, string titleAr, string titleEn, decimal maxPoints, CancellationToken ct);

    /// <summary>The write guard on its own (locked gradebook, closed class or year): 409 as a <see cref="GradebookRuleException"/>. Editors that change rows other than scores call it first.</summary>
    Task EnsureWritableAsync(Gradebook gradebook, CancellationToken ct);

    /// <summary>Sets a manual score. <paramref name="expectedVersion"/> is the version the caller read (null for a new entry).</summary>
    Task<GradeEntry> SetPointsAsync(
        GradeItem item, Guid studentProfileId, decimal? points, int? expectedVersion, Guid? byUserId, CancellationToken ct);
}
