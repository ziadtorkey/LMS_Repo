using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Lms.IntegrationTests.Infrastructure;

/// <summary>Test-only endpoint reporting the resolved current context.</summary>
public sealed class TestEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/test/scoped", (ICurrentContext ctx) =>
            ctx.ScopePath is null && !ctx.IsPlatformAdmin
                ? Results.Problem(statusCode: 403, title: "no context")
                : Results.Ok(new
                {
                    ctx.ScopePath, ctx.OrganizationId, ctx.AssignmentId, ctx.UserId, ctx.IsPlatformAdmin,
                }));

        endpoints.MapGet("/test/org-units/view", () => Results.Ok()).RequirePermission(Permissions.OrgUnits.View);
        endpoints.MapPost("/test/org-units/create", () => Results.Ok()).RequirePermission(Permissions.OrgUnits.Create);
        // Unit-targeted and client-root checks (the helpers M1 endpoints use).
        endpoints.MapPost("/test/units/{id:guid}/create-child", async (Guid id, HttpContext http, CancellationToken ct) =>
        {
            var access = await http.CheckUnitAsync(Permissions.OrgUnits.Create, id, ct);
            return access.Problem ?? Results.Ok(new { unitId = access.Unit!.Id });
        });
        endpoints.MapGet("/test/client-root/level-types", async (HttpContext http, CancellationToken ct) =>
            await http.CheckClientRootAsync(Permissions.LevelTypes.Create, ct) ?? Results.Ok());

        endpoints.MapGet("/test/relationship/class-course/{id:guid}", async (Guid id, HttpContext http, CancellationToken ct) =>
        {
            var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Classes.View, id, ct);
            return problem ?? Results.Ok(new { cc!.Id });
        });

        endpoints.MapGroup("/test/audit").RequirePermission(Permissions.Audit.View).MapGet("/", () => Results.Ok());
    }
}
