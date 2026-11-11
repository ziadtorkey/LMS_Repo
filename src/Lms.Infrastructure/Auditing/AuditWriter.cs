using Lms.Domain.Abstractions;
using Lms.Domain.Auditing;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Auditing;

/// <summary>
/// Writes security events through a separate DbContext so it never flushes (or fails with) the pending
/// changes of the calling context: a failed-login record must persist even when the surrounding work is abandoned.
/// </summary>
internal sealed class AuditWriter(
    DbContextOptions<LmsDbContext> options, ICurrentContext context, IClock clock) : IAuditWriter
{
    public async Task WriteSecurityEventAsync(
        string eventName, Guid? userId, string? details, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        await using var db = new LmsDbContext(options, context);
        var actor = userId ?? context.UserId;
        var now = clock.UtcNow;

        // D20: one entry per client in which the user holds a valid assignment, so each client's root-level viewers see
        // it. Users without one (platform admins, unknown emails) keep a single entry with the context's client (usually null).
        var organizations = actor is { } id
            ? await db.ValidAssignmentsFor(id, DateOnly.FromDateTime(now)).Select(a => a.OrganizationId).Distinct()
                .ToListAsync(cancellationToken)
            : [];

        if (organizations.Count == 0)
        {
            db.AuditEntries.Add(Entry(context.OrganizationId, context.AssignmentId));
        }
        else
        {
            db.AuditEntries.AddRange(organizations.Select(o =>
                Entry(o, context.OrganizationId == o ? context.AssignmentId : null)));
        }

        await db.SaveChangesAsync(cancellationToken);

        AuditEntry Entry(Guid? organizationId, Guid? assignmentId) => new(
            organizationId, actor, assignmentId, AuditAction.SecurityEvent,
            eventName, Guid.Empty, beforeJson: null, afterJson: details, now, ipAddress);
    }
}
