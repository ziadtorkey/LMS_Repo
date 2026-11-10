using Lms.Domain.Abstractions;
using Lms.Domain.Auditing;
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
        db.AuditEntries.Add(new AuditEntry(
            context.OrganizationId, userId ?? context.UserId, context.AssignmentId, AuditAction.SecurityEvent,
            eventName, Guid.Empty, beforeJson: null, afterJson: details, clock.UtcNow, ipAddress));
        await db.SaveChangesAsync(cancellationToken);
    }
}
