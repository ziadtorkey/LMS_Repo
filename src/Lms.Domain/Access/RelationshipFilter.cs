namespace Lms.Domain.Access;

/// <summary>
/// The resources a caller reaches through a relationship (PRD §3.3 step 2): a teacher's own class courses, a
/// supervisor's courses, a student's own enrollments. <see cref="IsUnrestricted"/> for roles without a rule.
/// </summary>
public sealed class RelationshipFilter
{
    private static readonly IReadOnlySet<Guid> NoIds = new HashSet<Guid>();

    public RelationshipFilter(
        bool isUnrestricted, IReadOnlySet<Guid> classCourseIds, IReadOnlySet<Guid> classIds, IReadOnlySet<Guid> courseIds,
        IReadOnlySet<Guid> homeroomClassIds)
    {
        IsUnrestricted = isUnrestricted;
        ClassCourseIds = classCourseIds;
        ClassIds = classIds;
        CourseIds = courseIds;
        HomeroomClassIds = homeroomClassIds;
    }

    public static RelationshipFilter Unrestricted { get; } = new(true, NoIds, NoIds, NoIds, NoIds);

    /// <summary>A restricted caller with no relationships: reaches nothing.</summary>
    public static RelationshipFilter None { get; } = new(false, NoIds, NoIds, NoIds, NoIds);

    public bool IsUnrestricted { get; }

    /// <summary>Class courses reached directly (own teaching, supervised courses, own enrollments).</summary>
    public IReadOnlySet<Guid> ClassCourseIds { get; }

    /// <summary>Classes of those class courses plus the classes where the caller is homeroom teacher.</summary>
    public IReadOnlySet<Guid> ClassIds { get; }

    /// <summary>Courses of those class courses plus the courses the caller supervises.</summary>
    public IReadOnlySet<Guid> CourseIds { get; }

    public IReadOnlySet<Guid> HomeroomClassIds { get; }

    public bool AllowsClassCourse(Guid id) => IsUnrestricted || ClassCourseIds.Contains(id);

    public bool AllowsClass(Guid id) => IsUnrestricted || ClassIds.Contains(id);

    public bool AllowsCourse(Guid id) => IsUnrestricted || CourseIds.Contains(id);
}
