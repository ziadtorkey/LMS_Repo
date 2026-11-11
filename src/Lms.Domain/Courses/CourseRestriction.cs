using System.Text.Json;
using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Courses;

/// <summary>One restriction type of a course (religion, gender, level type); the values are any-of. One row per type.</summary>
[Audited]
public sealed class CourseRestriction : UnitOwnedEntity
{
    private CourseRestriction()
    {
        Type = ValuesJson = string.Empty;
    }

    public CourseRestriction(Course course, string type, IReadOnlyList<string> values)
    {
        PlaceLike(course);
        CourseId = course.Id;
        Type = type;
        ValuesJson = JsonSerializer.Serialize(values);
    }

    public Guid CourseId { get; private set; }
    public string Type { get; private set; }
    public string ValuesJson { get; private set; }

    public IReadOnlyList<string> Values => JsonSerializer.Deserialize<List<string>>(ValuesJson) ?? [];

    public void SetValues(IReadOnlyList<string> values) => ValuesJson = JsonSerializer.Serialize(values);
}
