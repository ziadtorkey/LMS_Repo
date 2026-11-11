using Lms.Domain.Abstractions;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Access;

/// <summary>
/// The permissions the caller holds through the role of the active context; used by role and assignment admin to
/// refuse privilege escalation (a caller may only hand out what they hold). A platform admin holds every client permission.
/// </summary>
public sealed class CallerPermissions(LmsDbContext db, ICurrentContext context)
{
    public bool IsPlatformAdmin => context.IsPlatformAdmin;

    public async Task<IReadOnlySet<string>> GetAsync(CancellationToken cancellationToken)
    {
        if (context.IsPlatformAdmin)
        {
            return Permissions.All.Except(Permissions.PlatformOnly).ToHashSet(StringComparer.Ordinal);
        }

        if (context.AssignmentId is not { } assignmentId)
        {
            return new HashSet<string>();
        }

        var roleId = await db.RoleAssignments.Where(a => a.Id == assignmentId).Select(a => a.RoleId)
            .FirstOrDefaultAsync(cancellationToken);
        return (await db.RolePermissions.Where(p => p.RoleId == roleId).Select(p => p.Permission)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
    }
}
