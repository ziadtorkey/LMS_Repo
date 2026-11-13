using Lms.Api.Infrastructure;
using Lms.Infrastructure.Packages;
using Microsoft.Extensions.Options;

namespace Lms.Api.PackageHost;

/// <summary>Registers the services of the package host.</summary>
public sealed class PackageHostModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddSingleton<PackageTokenService>();
        services.AddSingleton<PackageSystemContexts>();
        services.AddSingleton<PackageRevocationCache>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
    }
}

/// <summary>
/// The separate pipeline of the content host (ADR 0005, D56). Requests whose host and port are the configured <c>PackageHost:Origin</c>
/// leave the app pipeline at once (no cookies, no auth, no app routes, no app headers) and reach only <c>/p/{token}/{**path}</c>; every
/// other path there is a 404 with the same headers. When the origin is missing, equals the app origin or shares its registrable domain
/// (<see cref="PackageHostOptions.IsConfigured"/>) the branch is <b>not mapped</b>: there is no fallback to the app origin.
/// </summary>
public static class PackageHostPipeline
{
    public static IApplicationBuilder UsePackageHost(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<PackageHostOptions>>().Value;
        if (!options.IsConfigured)
        {
            return app;
        }

        var origin = new Uri(options.Origin!);
        return app.MapWhen(context => Matches(context.Request, origin), branch =>
        {
            branch.UseMiddleware<PackageHostHeadersMiddleware>();
            branch.UseRouting();
            branch.UseEndpoints(endpoints =>
            {
                endpoints.MapMethods(PackageShimEndpoint.Pattern, ["GET", "HEAD"], PackageShimEndpoint.Handle);
                endpoints.MapMethods(PackageFileEndpoint.Pattern, ["GET", "HEAD"], PackageFileEndpoint.HandleAsync);
            });
            branch.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            });
        });
    }

    private static bool Matches(HttpRequest request, Uri origin)
    {
        var port = request.Host.Port ?? (request.IsHttps ? 443 : 80);
        return string.Equals(request.Host.Host, origin.Host, StringComparison.OrdinalIgnoreCase) && port == origin.Port;
    }
}
