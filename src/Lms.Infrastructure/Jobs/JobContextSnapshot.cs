using Lms.Domain.Abstractions;

namespace Lms.Infrastructure.Jobs;

/// <summary>
/// The caller context a background job runs with: captured when the job is enqueued and restored when it runs, so the
/// global query filters (client, subtree, soft delete) apply inside jobs exactly as they did for the enqueuing request.
/// </summary>
public sealed record JobContextSnapshot(
    Guid? UserId, Guid? OrganizationId, Guid? AssignmentId, string? ScopePath, bool IsPlatformAdmin)
{
    /// <summary>Name of the job parameter that carries the serialized snapshot.</summary>
    public const string ParameterName = "lms.context";

    /// <summary>
    /// The explicit platform context of system jobs (recurring maintenance, expiry): sees every client, acts as no user.
    /// </summary>
    public static JobContextSnapshot System { get; } = new(null, null, null, null, true);

    public static JobContextSnapshot From(ICurrentContext context) =>
        new(context.UserId, context.OrganizationId, context.AssignmentId, context.ScopePath, context.IsPlatformAdmin);
}
