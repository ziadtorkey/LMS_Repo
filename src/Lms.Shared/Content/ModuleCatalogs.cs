namespace Lms.Shared.Content;

/// <summary>
/// Lesson module kinds (FR-CNT-01). LTI is out of v1 (D50). <see cref="All"/> is the full v1 catalog so names never
/// change; <see cref="Content"/> are the kinds whose content is created through the module API (M4a wave 1). The other
/// kinds are created by their own feature (an assignment, a quiz, a library resource, a live session, a package),
/// which then owns the module's <c>RefId</c>; the module API answers 400 <c>validation_failed</c> for them.
/// </summary>
public static class ModuleKinds
{
    public const string File = "File";
    public const string Video = "Video";
    public const string Audio = "Audio";
    public const string Page = "Page";
    public const string Link = "Link";
    public const string Assignment = "Assignment";
    public const string Quiz = "Quiz";
    public const string LibraryResource = "LibraryResource";
    public const string LiveSession = "LiveSession";
    public const string Scorm = "Scorm";
    public const string H5p = "H5p";

    public static IReadOnlyList<string> All { get; } =
        [File, Video, Audio, Page, Link, Assignment, Quiz, LibraryResource, LiveSession, Scorm, H5p];

    /// <summary>Kinds whose content is the asset, rich text or URL carried by <see cref="ModuleContentInput"/>.</summary>
    public static IReadOnlyList<string> Content { get; } = [File, Video, Audio, Page, Link];
}

/// <summary>
/// Module visibility (FR-CNT-02). <c>Scheduled</c> is visible to students only inside
/// [<c>VisibleFromUtc</c>, <c>VisibleToUtc</c>) (either end may be open, not both).
/// </summary>
public static class ModuleVisibilities
{
    public const string Published = "Published";
    public const string Hidden = "Hidden";
    public const string Scheduled = "Scheduled";

    public static IReadOnlyList<string> All { get; } = [Published, Hidden, Scheduled];
}

/// <summary>Who a module is for (FR-CNT-02, D51): the whole ClassCourse, or one of its groups and that ClassCourse's teachers.</summary>
public static class ModuleTargets
{
    public const string ClassCourse = "ClassCourse";
    public const string Group = "Group";

    public static IReadOnlyList<string> All { get; } = [ClassCourse, Group];
}

/// <summary>
/// How a module becomes complete (FR-CNT-02, FR-CNT-04). <c>None</c> = not counted in progress; <c>Manual</c> = the
/// student marks it (PUT completion); <c>View</c> = opening it (GET of the module by the student); <c>Submit</c> =
/// a submission (assignments); <c>Score</c> = a score at or above <c>CompletionMinScore</c> (quizzes, packages).
/// </summary>
public static class CompletionRules
{
    public const string None = "None";
    public const string Manual = "Manual";
    public const string View = "View";
    public const string Submit = "Submit";
    public const string Score = "Score";

    public static IReadOnlyList<string> All { get; } = [None, Manual, View, Submit, Score];

    /// <summary>The rules a content kind (<see cref="ModuleKinds.Content"/>) may use.</summary>
    public static IReadOnlyList<string> ForContent { get; } = [None, Manual, View];
}

/// <summary>A student's completion state of one module.</summary>
public static class CompletionStates
{
    public const string NotStarted = "NotStarted";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";

    public static IReadOnlyList<string> All { get; } = [NotStarted, InProgress, Completed];
}
