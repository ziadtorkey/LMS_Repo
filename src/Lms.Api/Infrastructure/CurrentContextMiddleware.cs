using Lms.Domain.Abstractions;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Infrastructure;

/// <summary>
/// Resolves the caller's active context from the X-Lms-Context header (a RoleAssignment id).
/// Unauthenticated requests pass through with an empty context. A missing header leaves the context empty
/// (scoped data queries return nothing; endpoints requiring a permission reject it); an invalid header is a 403.
/// </summary>
public sealed class CurrentContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext http, HttpCurrentContext current, LmsDbContext db, IClock clock)
    {
        var userId = ReadUserId(http);
        if (userId is null)
        {
            await next(http);
            return;
        }

        var isAdmin = http.User.HasClaim("platform_admin", "true");
        current.SetUser(userId.Value, isAdmin);

        var header = http.Request.Headers[ContextHeader.Name];
        if (header.Count == 0)
        {
            await next(http);
            return;
        }

        if (header.Count > 1 || !Guid.TryParse(header[0], out var assignmentId) || assignmentId == Guid.Empty)
        {
            await RejectAsync(http);
            return;
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);

        var assignment = await db.ValidAssignmentsFor(userId.Value, today)
            .Where(a => a.AssignmentId == assignmentId)
            .FirstOrDefaultAsync(http.RequestAborted);
        if (assignment is null)
        {
            await RejectAsync(http);
            return;
        }

        current.SetAssignment(assignmentId, assignment.OrganizationId, assignment.OrgUnitPath);
        await next(http);
    }

    private static Guid? ReadUserId(HttpContext http)
    {
        if (http.User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var sub = http.User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private static Task RejectAsync(HttpContext http) =>
        Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Invalid context",
                detail: "The active context is missing, expired or does not belong to the caller.",
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_context" })
            .ExecuteAsync(http);
}