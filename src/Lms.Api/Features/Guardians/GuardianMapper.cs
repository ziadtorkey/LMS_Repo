using Lms.Domain.People;
using Lms.Infrastructure.People.Guardians;
using Lms.Infrastructure.Persistence;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Guardians;

/// <summary>Reads of guardians for the API. Every guardian read starts from the caller's scope through <see cref="GuardianVisibility"/>.</summary>
internal static class GuardianMapper
{
    public static async Task<IReadOnlyList<StudentGuardianDto>> LinksOfAsync(LmsDbContext db, StudentProfile student, CancellationToken ct)
    {
        var links = await db.Set<StudentGuardian>().Where(l => l.StudentId == student.Id)
            .OrderByDescending(l => l.IsPrimary).ThenBy(l => l.CreatedAt).ToListAsync(ct);
        var ids = links.Select(l => l.GuardianId).ToList();
        var guardians = await GuardianVisibility.Visible(db).Where(g => ids.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);
        var siblings = await db.Set<StudentGuardian>().Where(l => ids.Contains(l.GuardianId) && l.StudentId != student.Id)
            .GroupBy(l => l.GuardianId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        return links.Where(l => guardians.ContainsKey(l.GuardianId)).Select(l =>
        {
            var g = guardians[l.GuardianId];
            return new StudentGuardianDto(
                g.Id, g.NameAr, g.NameEn, l.Relation, l.IsPrimary, g.Mobile, g.Email, g.NationalId, g.UserId is not null,
                siblings.GetValueOrDefault(g.Id));
        }).ToList();
    }

    public static async Task<Dictionary<Guid, int>> ChildrenInScopeAsync(LmsDbContext db, IReadOnlyCollection<Guid> guardianIds, CancellationToken ct) =>
        await db.Set<StudentGuardian>().Where(l => guardianIds.Contains(l.GuardianId))
            .GroupBy(l => l.GuardianId).Select(g => new { g.Key, Count = g.Select(l => l.StudentId).Distinct().Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

    public static GuardianSummaryDto Summary(GuardianProfile g, int children) =>
        new(g.Id, g.NameAr, g.NameEn, g.Mobile, g.Email, children, g.UserId is not null);

    /// <summary>Keeps the first <c>+</c> and the last 3 digits (e.g. <c>+*********123</c>).</summary>
    public static string MaskMobile(string mobile)
    {
        if (mobile.Length <= 4)
        {
            return new string('*', mobile.Length);
        }

        var prefix = mobile.StartsWith('+') ? "+" : string.Empty;
        return prefix + new string('*', mobile.Length - prefix.Length - 3) + mobile[^3..];
    }
}
