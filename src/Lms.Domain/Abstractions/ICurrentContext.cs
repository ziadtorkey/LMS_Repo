namespace Lms.Domain.Abstractions;

/// <summary>The caller's active context: who, which client, which role assignment and subtree.</summary>
public interface ICurrentContext
{
    Guid? UserId { get; }
    Guid? OrganizationId { get; }
    Guid? AssignmentId { get; }
    /// <summary>Path of the org unit of the active assignment; null means "no scope" (scoped queries return nothing).</summary>
    string? ScopePath { get; }
    bool IsPlatformAdmin { get; }
}