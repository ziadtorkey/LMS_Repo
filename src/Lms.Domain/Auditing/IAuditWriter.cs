namespace Lms.Domain.Auditing;

/// <summary>Writes audit entries that are not tied to an entity change (login success/failure, lockout...).</summary>
public interface IAuditWriter
{
    Task WriteSecurityEventAsync(string eventName, Guid? userId, string? details, CancellationToken cancellationToken = default);
}
