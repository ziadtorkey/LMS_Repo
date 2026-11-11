namespace Lms.Shared.Enrollment;

/// <summary>
/// A student group of a ClassCourse (FR-GRP-01..03). A student is in at most one group per ClassCourse (D6).
/// <c>MatchType</c>/<c>MatchValue</c> (a <c>CourseRestrictionTypes</c> value and one of its values) drive suggestions.
/// </summary>
public sealed record StudentGroupDto(
    Guid Id,
    Guid ClassCourseId,
    string NameAr,
    string NameEn,
    string? MatchType,
    string? MatchValue,
    int MemberCount);

/// <summary>Body of POST /api/class-courses/{id}/groups and PUT /api/groups/{id}.</summary>
public sealed record SaveGroupRequest(string NameAr, string NameEn, string? MatchType, string? MatchValue);

/// <summary>Body of POST /api/groups/{id}/members and /members/remove; students must be enrolled in the ClassCourse.</summary>
public sealed record GroupMembersRequest(IReadOnlyList<Guid> StudentIds);

public sealed record GroupMemberDto(Guid StudentId, string StudentCode, string NameAr, string NameEn);
