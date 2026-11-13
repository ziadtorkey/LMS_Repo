namespace Lms.Domain.Content;

/// <summary>
/// The scheduling side of a module: title, visibility window, target and completion rule. Shared by the module API and by
/// the features whose kind owns the module's content (assignments, quizzes, library resources, live sessions, packages).
/// </summary>
public sealed record ModuleMetadata(
    string TitleAr,
    string TitleEn,
    string Visibility,
    DateTime? VisibleFromUtc,
    DateTime? VisibleToUtc,
    string Target,
    Guid? GroupId,
    string CompletionRule,
    decimal? CompletionMinScore);

/// <summary>A module request broke a rule; <see cref="Code"/> is the ProblemDetails code the API answers with.</summary>
public sealed class ModuleRuleException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// The seam feature kinds use to put their activity into a lesson (M4a plan Task 3); they never edit the content module.
/// Every call works on the scoped DbContext, so inside the caller's transaction it commits or rolls back with the caller.
/// The caller has already checked the permission and the relationship to the class course. Closed classes and years are
/// refused with the enrollment rule exceptions; a bad visibility range or target with <see cref="ModuleRuleException"/>.
/// </summary>
public interface IModuleWriter
{
    /// <summary>Creates the module of a feature kind (not a content kind), appended last in the lesson; returns its id.</summary>
    Task<Guid> CreateForRefAsync(
        Guid classCourseId, Guid lessonKey, string kind, Guid refId, ModuleMetadata metadata, CancellationToken ct = default);

    Task UpdateMetadataAsync(Guid moduleId, ModuleMetadata metadata, CancellationToken ct = default);

    /// <summary>Soft-deletes the module that points at the activity (no-op when there is none).</summary>
    Task DeleteForRefAsync(string kind, Guid refId, CancellationToken ct = default);
}
