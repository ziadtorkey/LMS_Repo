namespace Lms.Shared.Grading;

// M5 contract, wave 1 (M5 plan Task 1): grading scales, gradebook, publishing, grade reads, reports. D58, D59.

/// <summary>How a scale expresses a grade: a bare percentage, letters (A-F), or a GPA (points per band).</summary>
public static class ScaleKinds
{
    public const string Percentage = "Percentage";
    public const string Letter = "Letter";
    public const string Gpa = "Gpa";

    public static IReadOnlyList<string> All { get; } = [Percentage, Letter, Gpa];
}

/// <summary>Whether students and guardians can see a grade item (FR-GRD-05).</summary>
public static class PublishStates
{
    /// <summary>The default of every new and every event-created item (L3): staff only.</summary>
    public const string Hidden = "Hidden";

    public const string Published = "Published";

    /// <summary>Becomes <c>Published</c> at <c>PublishAtUtc</c> by the scheduled job (server clock).</summary>
    public const string Scheduled = "Scheduled";

    public static IReadOnlyList<string> All { get; } = [Hidden, Published, Scheduled];
}

/// <summary>Where a grid cell's value comes from.</summary>
public static class GradeCellStates
{
    /// <summary>The score of a grade event (assignment, quiz, package): read only in the grid.</summary>
    public const string Event = "Event";

    /// <summary>Typed by the teacher into a manual item.</summary>
    public const string Manual = "Manual";

    /// <summary>The teacher replaced an event score; the override needs a reason (audited).</summary>
    public const string Override = "Override";

    /// <summary>An override exists and a newer event score arrived: staff decide to keep or drop it (staff only, never shown to students).</summary>
    public const string NewerScoreExists = "NewerScoreExists";

    /// <summary>Excused: the item does not count for this student.</summary>
    public const string Excused = "Excused";

    public const string Empty = "Empty";

    public static IReadOnlyList<string> All { get; } = [Event, Manual, Override, NewerScoreExists, Excused, Empty];
}

/// <summary>What the engine does with an item that has no score for a student (setting <c>Grades.MissingPolicy</c>).</summary>
public static class MissingPolicies
{
    /// <summary>The total is normalised over the categories that have scores (default).</summary>
    public const string Normalise = "Normalise";

    /// <summary>A missing score counts as 0.</summary>
    public const string Zero = "Zero";

    /// <summary>No total is shown until every item is graded.</summary>
    public const string HideUntilComplete = "HideUntilComplete";

    public static IReadOnlyList<string> All { get; } = [Normalise, Zero, HideUntilComplete];
}

/// <summary>How a category's items combine (setting <c>Grades.CategoryMethod</c>).</summary>
public static class CategoryMethods
{
    /// <summary>Sum of points over sum of maximum points (default).</summary>
    public const string PointsWeighted = "PointsWeighted";

    /// <summary>The mean of the items' percentages.</summary>
    public const string PercentAverage = "PercentAverage";

    public static IReadOnlyList<string> All { get; } = [PointsWeighted, PercentAverage];
}

public static class RoundingModes
{
    public const string HalfUp = "HalfUp";

    public static IReadOnlyList<string> All { get; } = [HalfUp];
}

/// <summary>A locked gradebook (L5) serves its lock snapshot and refuses writes (409 <c>gradebook_locked</c>); M7 adds the button.</summary>
public static class GradebookStates
{
    public const string Open = "Open";
    public const string Locked = "Locked";

    public static IReadOnlyList<string> All { get; } = [Open, Locked];
}

/// <summary><see cref="PublishItemsRequest"/> action.</summary>
public static class PublishModes
{
    public const string Now = "Now";
    public const string Schedule = "Schedule";

    /// <summary>Un-publishes: the items go back to <c>Hidden</c> (audited).</summary>
    public const string Hide = "Hide";

    public static IReadOnlyList<string> All { get; } = [Now, Schedule, Hide];
}

/// <summary>What one entry change does (<see cref="GradeEntryChange"/>).</summary>
public static class GradeEntryActions
{
    /// <summary>Set the score of a manual item.</summary>
    public const string Set = "Set";

    /// <summary>Replace an event score; <c>Reason</c> required.</summary>
    public const string Override = "Override";

    /// <summary>Remove the override: the event score counts again.</summary>
    public const string ClearOverride = "ClearOverride";

    /// <summary>Mark the item excused for the student.</summary>
    public const string Excuse = "Excuse";

    /// <summary>Remove an excuse or a manual score.</summary>
    public const string Clear = "Clear";

    /// <summary>On <c>NewerScoreExists</c>: keep the override and dismiss the marker.</summary>
    public const string KeepOverride = "KeepOverride";

    /// <summary>On <c>NewerScoreExists</c>: drop the override, the newer event score counts.</summary>
    public const string DropOverride = "DropOverride";

    public static IReadOnlyList<string> All { get; } = [Set, Override, ClearOverride, Excuse, Clear, KeepOverride, DropOverride];
}

/// <summary>What a stored <c>GradeSnapshot</c> froze: the values published to a student in a batch, or the values at a lock (L5).</summary>
public static class GradeSnapshotKinds
{
    public const string Publish = "Publish";
    public const string Lock = "Lock";

    public static IReadOnlyList<string> All { get; } = [Publish, Lock];
}

/// <summary>The three report scopes of <c>GET /api/grade-reports/{kind}</c>.</summary>
public static class GradeReportKinds
{
    public const string Student = "student";
    public const string Class = "class";
    public const string Course = "course";

    public static IReadOnlyList<string> All { get; } = [Student, Class, Course];
}

/// <summary>Limits of the grading contract (also enforced by the server).</summary>
public static class GradingLimits
{
    public const int DecimalPlacesMin = 0;
    public const int DecimalPlacesMax = 4;
    public const int DecimalPlacesDefault = 2;

    public const decimal WeightMin = 0m;
    public const decimal WeightMax = 100m;

    public const decimal MaxPointsMin = 0.01m;
    public const decimal MaxPointsMax = 1000m;

    /// <summary>Length of an override reason or an unlocking reason.</summary>
    public const int ReasonMinLength = 1;

    public const int ReasonMaxLength = 500;

    public const int NameMaxLength = 200;

    /// <summary>Most bands of one scale.</summary>
    public const int MaxBands = 20;

    /// <summary>The grid pages by this many students (L17).</summary>
    public const int GridPageSize = 50;

    public const int GridPageSizeMax = 100;

    /// <summary>Grade column import (L9): rows, columns and how long a preview token lives.</summary>
    public const int ImportMaxRows = 2000;

    public const int ImportMaxColumns = 50;

    public const int ImportTokenMinutes = 15;

    /// <summary>The setting values of terms per year (D62).</summary>
    public const int TermsPerYearMin = 2;

    public const int TermsPerYearMax = 3;
}
