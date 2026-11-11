using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Enrollment;

/// <summary>A group of students inside one class course (a student is in at most one group per class course, D6).</summary>
[Audited]
public sealed class StudentGroup : UnitOwnedEntity
{
    private StudentGroup()
    {
        NameAr = NameEn = string.Empty;
    }

    public StudentGroup(ClassCourse classCourse, string nameAr, string nameEn, string? matchType, string? matchValue)
    {
        PlaceLike(classCourse);
        ClassCourseId = classCourse.Id;
        NameAr = nameAr.Trim();
        NameEn = nameEn.Trim();
        MatchType = matchType;
        MatchValue = matchValue;
    }

    public Guid ClassCourseId { get; private set; }
    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public string? MatchType { get; private set; }
    public string? MatchValue { get; private set; }

    public void Update(string nameAr, string nameEn, string? matchType, string? matchValue)
    {
        NameAr = nameAr.Trim();
        NameEn = nameEn.Trim();
        MatchType = matchType;
        MatchValue = matchValue;
    }
}
