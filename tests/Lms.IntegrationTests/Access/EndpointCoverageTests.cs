using Lms.Api.Infrastructure;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Access;

/// <summary>Safety net: a new /api endpoint cannot ship without a declared permission (or an explicit exemption).</summary>
[Collection(ApiCollection.Name)]
public sealed class EndpointCoverageTests(LmsApiFactory factory)
{
    /// <summary>Endpoints that intentionally have no RequirePermission, each with the reason.</summary>
    private static readonly Dictionary<string, string> Exemptions = new()
    {
        ["/api/{**path}"] = "404 fallback for unknown API routes",
        ["/api/me"] = "authentication only: the caller lists their own contexts before choosing one",
        ["/api/me/permissions"] = "authentication + valid context; returns the caller's own permissions",
    };

    [Fact]
    public void Every_api_endpoint_declares_a_permission_or_is_explicitly_exempt()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && raw.StartsWith("/api", StringComparison.OrdinalIgnoreCase));

        var unprotected = endpoints
            .Where(e => e.Metadata.GetMetadata<RequiresPermissionMetadata>() is null)
            .Select(e => e.RoutePattern.RawText!)
            .Where(route => !Exemptions.ContainsKey(route))
            .ToList();

        Assert.Empty(unprotected);
    }

    [Fact]
    public void Protected_endpoints_expose_their_permission_as_metadata()
    {
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/test/org-units/view");

        Assert.Equal(
            Lms.Shared.Authorization.Permissions.OrgUnits.View,
            endpoint.Metadata.GetMetadata<RequiresPermissionMetadata>()!.Permission);
    }
}