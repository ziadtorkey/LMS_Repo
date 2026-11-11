namespace Lms.Domain.Courses;

/// <summary>Pure restriction check: AND across restriction types, any-of within one type (an empty list passes everyone).</summary>
public static class CourseRestrictionEvaluator
{
    public const string Religion = "Religion";
    public const string Gender = "Gender";
    public const string LevelType = "LevelType";

    /// <returns>The restriction types the student fails (empty = may enrol).</returns>
    public static IReadOnlyList<string> Failed(
        IReadOnlyList<(string Type, IReadOnlyList<string> Values)> restrictions, string gender, string religion, Guid classUnitLevelTypeId)
    {
        var failed = new List<string>();
        foreach (var (type, values) in restrictions)
        {
            if (values.Count == 0)
            {
                continue;
            }

            var actual = type switch
            {
                Religion => religion,
                Gender => gender,
                LevelType => classUnitLevelTypeId.ToString(),
                _ => null,
            };

            if (actual is null || !values.Contains(actual, StringComparer.OrdinalIgnoreCase))
            {
                failed.Add(type);
            }
        }

        return failed;
    }
}
