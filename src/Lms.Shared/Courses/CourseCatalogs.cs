namespace Lms.Shared.Courses;

public static class CourseStatuses
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";

    public static IReadOnlyList<string> All { get; } = [Active, Inactive];
}

/// <summary>
/// Course restriction types (FR-CRS-03, Q3). Religion/Gender values are <c>Religions</c>/<c>Genders</c> keys; LevelType
/// values are level type ids.
/// </summary>
public static class CourseRestrictionTypes
{
    public const string Religion = "Religion";
    public const string Gender = "Gender";
    public const string LevelType = "LevelType";

    public static IReadOnlyList<string> All { get; } = [Religion, Gender, LevelType];
}

/// <summary>Accepted values of <c>CourseQuery.Sort</c> (a leading <c>-</c> sorts descending).</summary>
public static class CourseSorts
{
    public const string Code = "code";
    public const string CodeDesc = "-code";
    public const string Name = "name";
    public const string NameDesc = "-name";

    public static IReadOnlyList<string> All { get; } = [Code, CodeDesc, Name, NameDesc];
}
