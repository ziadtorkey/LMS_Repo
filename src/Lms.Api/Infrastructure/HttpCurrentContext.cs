using Lms.Domain.Abstractions;

namespace Lms.Api.Infrastructure;

/// <summary>Per-request caller context, populated by <see cref="CurrentContextMiddleware"/>. Empty until then.</summary>
public sealed class HttpCurrentContext : ICurrentContext
{
    public Guid? UserId { get; private set; }
    public Guid? OrganizationId { get; private set; }
    public Guid? AssignmentId { get; private set; }
    public string? ScopePath { get; private set; }
    public bool IsPlatformAdmin { get; private set; }

    public void SetUser(Guid userId, bool isPlatformAdmin)
    {
        UserId = userId;
        IsPlatformAdmin = isPlatformAdmin;
    }

    public void SetAssignment(Guid assignmentId, Guid organizationId, string scopePath)
    {
        AssignmentId = assignmentId;
        OrganizationId = organizationId;
        ScopePath = scopePath;
        // Acting inside a chosen context never carries platform-wide rights.
        IsPlatformAdmin = false;
    }
}