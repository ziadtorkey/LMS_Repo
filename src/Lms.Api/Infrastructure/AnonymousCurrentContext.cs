using Lms.Domain.Abstractions;

namespace Lms.Api.Infrastructure;

/// <summary>Placeholder until the real HTTP-based context (roles/assignments task) replaces it: no caller, no scope.</summary>
public sealed class AnonymousCurrentContext : ICurrentContext
{
    public Guid? UserId => null;
    public Guid? OrganizationId => null;
    public Guid? AssignmentId => null;
    public string? ScopePath => null;
    public bool IsPlatformAdmin => false;
}