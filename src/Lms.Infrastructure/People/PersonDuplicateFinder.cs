using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.People;

/// <summary>
/// Client-wide duplicate lookups (M2 Global Constraints). Every query ignores the caller's unit scope on purpose and
/// restricts itself to one client and to live rows by hand; callers learn only which field collided.
/// </summary>
public sealed class PersonDuplicateFinder(LmsDbContext db) : IPersonDuplicateFinder
{
    public async Task<string?> FindStaffConflictAsync(
        Guid organizationId, string email, string? mobile, string? nationalId, Guid? excludeProfileId, CancellationToken ct)
    {
        // ISOLATION-BYPASS: a duplicate may sit in a sibling subtree outside the caller's scope. The client is matched
        // and deleted rows are excluded explicitly; only the colliding field name leaves this method.
        var others = db.Set<StaffProfile>().IgnoreQueryFilters()
            .Where(p => p.OrganizationId == organizationId && !p.IsDeleted && p.Id != excludeProfileId);

        if (await others.AnyAsync(p => p.Email == email, ct))
        {
            return "email";
        }

        if (mobile is not null && await others.AnyAsync(p => p.Mobile == mobile, ct))
        {
            return "mobile";
        }

        if (nationalId is not null && await others.AnyAsync(p => p.NationalId == nationalId, ct))
        {
            return "nationalId";
        }

        return null;
    }

    public Task<bool> StudentNationalIdExistsAsync(Guid organizationId, string nationalId, Guid? excludeId, CancellationToken ct) =>
        // ISOLATION-BYPASS: client-wide check across sibling subtrees; client matched and deleted rows excluded by hand.
        db.Set<StudentProfile>().IgnoreQueryFilters()
            .AnyAsync(s => s.OrganizationId == organizationId && !s.IsDeleted && s.Id != excludeId && s.NationalId == nationalId, ct);

    public Task<int> CountStudentsByNameAndDobAsync(
        Guid organizationId, string nameArKey, string nameEnKey, DateOnly dob, Guid? excludeId, CancellationToken ct) =>
        // ISOLATION-BYPASS: client-wide check across sibling subtrees; client matched and deleted rows excluded by hand.
        db.Set<StudentProfile>().IgnoreQueryFilters()
            .CountAsync(
                s => s.OrganizationId == organizationId && !s.IsDeleted && s.Id != excludeId && s.DateOfBirth == dob
                     && (s.NameArKey == nameArKey || s.NameEnKey == nameEnKey),
                ct);

    public async Task<GuardianProfile?> FindGuardianAsync(
        Guid organizationId, string? nationalId, string? mobile, CancellationToken ct)
    {
        // ISOLATION-BYPASS: guardians are client-wide; the client is matched and deleted rows excluded by hand.
        var guardians = db.Set<GuardianProfile>().IgnoreQueryFilters().Where(g => g.OrganizationId == organizationId && !g.IsDeleted);

        if (nationalId is not null
            && await guardians.OrderBy(g => g.CreatedAt).FirstOrDefaultAsync(g => g.NationalId == nationalId, ct) is { } byId)
        {
            return byId;
        }

        return mobile is null ? null : await guardians.OrderBy(g => g.CreatedAt).FirstOrDefaultAsync(g => g.Mobile == mobile, ct);
    }
}
