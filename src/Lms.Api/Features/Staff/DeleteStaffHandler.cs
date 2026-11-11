using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Staff;

/// <summary>DELETE /api/staff/{id}: soft-deletes the profile and the person's Staff-kind assignments in this client.</summary>
public sealed class DeleteStaffHandler(LmsDbContext db, ICurrentContext context, IPersonStatusService statuses)
{
    public async Task<IResult> DeleteAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var (profile, problem) = await StaffAccess.FindAsync(db, context, http, id, Permissions.Staff.Delete, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (profile!.UserId == context.UserId)
        {
            return ApiProblems.Conflict(ProblemCodes.cannot_change_own_status);
        }

        var organizationId = profile.OrganizationId;

        // ISOLATION-BYPASS: the person's shares point at units outside the scope of the admin who deletes them, and all of
        // them must go. Limited to this client and live rows, explicit.
        var live = db.RoleAssignments.IgnoreQueryFilters()
            .Where(a => a.UserId == profile.UserId && a.OrganizationId == organizationId && !a.IsDeleted);
        // ISOLATION-BYPASS: same reason; only the Staff-kind roles of this client (student and guardian roles stay).
        var staffRoles = db.Roles.IgnoreQueryFilters()
            .Where(r => r.OrganizationId == organizationId && r.BaseKind == RoleBaseKind.Staff).Select(r => r.Id);
        var assignments = await live.Where(a => staffRoles.Contains(a.RoleId)).ToListAsync(ct);

        db.RoleAssignments.RemoveRange(assignments); // soft delete
        db.Remove(profile);
        await db.SaveChangesAsync(ct);

        // The deleted profile no longer counts for the account's status (other clients' profiles still do).
        await statuses.RecomputeUserStatusAsync(profile.UserId, ct);
        return Results.NoContent();
    }
}
