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
public sealed class DeleteStaffHandler(
    LmsDbContext db, ICurrentContext context, IPersonStatusService statuses, Lms.Infrastructure.Access.CallerPermissions caller)
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

        if (await StaffAssignments.CheckCallerMayRemoveAccessAsync(db, caller, profile, ct) is { } refused)
        {
            return refused;
        }

        var assignments = await StaffAssignments.LiveAsync(db, profile, ct);

        db.RoleAssignments.RemoveRange(assignments); // soft delete
        db.Remove(profile);
        await db.SaveChangesAsync(ct);

        // The deleted profile no longer counts for the account's status (other clients' profiles still do).
        await statuses.RecomputeUserStatusAsync(profile.UserId, ct);
        return Results.NoContent();
    }
}
