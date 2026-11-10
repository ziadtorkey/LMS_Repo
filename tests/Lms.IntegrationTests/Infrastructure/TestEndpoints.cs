using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Lms.IntegrationTests.Infrastructure;

/// <summary>Test-only endpoint reporting the resolved current context.</summary>
public sealed class TestEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/test/scoped", (ICurrentContext ctx) =>
            ctx.ScopePath is null && !ctx.IsPlatformAdmin
                ? Results.Problem(statusCode: 403, title: "no context")
                : Results.Ok(new
                {
                    ctx.ScopePath, ctx.OrganizationId, ctx.AssignmentId, ctx.UserId, ctx.IsPlatformAdmin,
                }));
}