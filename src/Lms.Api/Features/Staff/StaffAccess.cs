using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Staff;

/// <summary>The lookup every staff write starts with: the profile inside the caller's scope, then the permission at its home.</summary>
internal static class StaffAccess
{
    public static async Task<(StaffProfile? Profile, IResult? Problem)> FindAsync(
        LmsDbContext db, ICurrentContext context, HttpContext http, Guid id, string permission, CancellationToken ct)
    {
        // Filtered set: a profile whose home is outside the caller's scope does not exist for the caller (404).
        // A platform admin without a client context acts on no client's staff.
        var profile = context.OrganizationId is null ? null : await db.Set<StaffProfile>().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null)
        {
            return (null, ApiProblems.NotFound());
        }

        var access = await http.CheckUnitAsync(permission, profile.OrgUnitId, ct);
        return access.Problem is not null ? (null, access.Problem) : (profile, null);
    }

    public static Task<(StaffProfile? Profile, IResult? Problem)> FindForUpdateAsync(
        LmsDbContext db, ICurrentContext context, HttpContext http, Guid id, CancellationToken ct) =>
        FindAsync(db, context, http, id, Permissions.Staff.Update, ct);
}
