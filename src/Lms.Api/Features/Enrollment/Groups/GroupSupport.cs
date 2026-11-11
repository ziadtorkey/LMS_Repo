using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Courses;
using Lms.Shared.Enrollment;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Enrollment.Groups;

/// <summary>Validation and mapping shared by the group endpoints.</summary>
internal static class GroupSupport
{
    public const int MaxMembersPerRequest = 500;
    private const int MaxName = 200;

    /// <summary>Names 1-200 characters in both languages; <c>MatchType</c>/<c>MatchValue</c> both null or a restriction pair of the catalogs.</summary>
    public static async Task<IResult?> ValidateAsync(SaveGroupRequest request, Guid organizationId, LmsDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.NameAr) || request.NameAr.Trim().Length > MaxName)
        {
            errors[nameof(request.NameAr)] = ["name_invalid"];
        }

        if (string.IsNullOrWhiteSpace(request.NameEn) || request.NameEn.Trim().Length > MaxName)
        {
            errors[nameof(request.NameEn)] = ["name_invalid"];
        }

        if ((request.MatchType is null) != (request.MatchValue is null))
        {
            errors[nameof(request.MatchValue)] = ["match_incomplete"];
        }
        else if (request.MatchType is { } type && request.MatchValue is { } value && !await MatchIsValidAsync(type, value, organizationId, db, ct))
        {
            errors[nameof(request.MatchValue)] = ["match_invalid"];
        }

        return errors.Count == 0
            ? null
            : Results.ValidationProblem(errors, extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });
    }

    private static async Task<bool> MatchIsValidAsync(string type, string value, Guid organizationId, LmsDbContext db, CancellationToken ct) =>
        type switch
        {
            CourseRestrictionTypes.Religion => Religions.All.Contains(value, StringComparer.OrdinalIgnoreCase),
            CourseRestrictionTypes.Gender => Genders.All.Contains(value, StringComparer.OrdinalIgnoreCase),
            CourseRestrictionTypes.LevelType => Guid.TryParse(value, out var id)
                && await db.Set<LevelType>().AnyAsync(l => l.Id == id && l.OrganizationId == organizationId, ct),
            _ => false,
        };

    public static StudentGroupDto ToDto(StudentGroup group, int members) =>
        new(group.Id, group.ClassCourseId, group.NameAr, group.NameEn, group.MatchType, group.MatchValue, members);

    public static async Task<Dictionary<Guid, int>> MemberCountsAsync(LmsDbContext db, IReadOnlyCollection<Guid> groupIds, CancellationToken ct) =>
        await db.Set<GroupMember>().Where(m => groupIds.Contains(m.GroupId))
            .GroupBy(m => m.GroupId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);

    public static async Task<ClassCourse?> ClassCourseAsync(LmsDbContext db, Guid id, CancellationToken ct) =>
        await db.Set<ClassCourse>().FirstOrDefaultAsync(c => c.Id == id, ct);
}
