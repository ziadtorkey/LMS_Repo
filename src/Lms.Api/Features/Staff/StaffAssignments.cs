using Lms.Api.Features.Assignments;
using Lms.Domain.Access;
using Lms.Domain.People;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Staff;

/// <summary>The Staff-kind role assignments of a person in their client, and the D16 guard that goes with removing access.</summary>
internal static class StaffAssignments
{
    /// <summary>
    /// The person's live Staff-kind assignments in their client, wherever they point (shares lie outside the scope of the admin
    /// who acts on the person).
    /// </summary>
    public static async Task<List<RoleAssignment>> LiveAsync(LmsDbContext db, StaffProfile profile, CancellationToken ct)
    {
        var organizationId = profile.OrganizationId;

        // ISOLATION-BYPASS: the person's shares point at units outside the scope of the admin who acts on them. Limited to this
        // client and live rows, explicit.
        var live = db.RoleAssignments.IgnoreQueryFilters()
            .Where(a => a.UserId == profile.UserId && a.OrganizationId == organizationId && !a.IsDeleted);
        // ISOLATION-BYPASS: same reason; only the Staff-kind roles of this client (student and guardian roles are not touched).
        var staffRoles = db.Roles.IgnoreQueryFilters()
            .Where(r => r.OrganizationId == organizationId && r.BaseKind == RoleBaseKind.Staff).Select(r => r.Id);
        return await live.Where(a => staffRoles.Contains(a.RoleId)).ToListAsync(ct);
    }

    /// <summary>
    /// Deactivating or deleting a person takes their access away, which is as strong as removing the assignments: the caller
    /// must be allowed to manage every role the person holds (D16, the strict-subset rule of the assignments API), otherwise a
    /// role with only Staff.Update could lock a client administrator out. Null when allowed, else the 403 to return.
    /// </summary>
    public static async Task<IResult?> CheckCallerMayRemoveAccessAsync(
        LmsDbContext db, CallerPermissions caller, StaffProfile profile, CancellationToken ct)
    {
        foreach (var roleId in (await LiveAsync(db, profile, ct)).Select(a => a.RoleId).Distinct())
        {
            if (!await AssignmentQueries.CallerMayManageRoleAsync(db, caller, roleId, ct))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                    extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.permission_escalation });
            }
        }

        return null;
    }
}
