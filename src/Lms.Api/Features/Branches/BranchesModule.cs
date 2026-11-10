using Lms.Infrastructure.Branches;

namespace Lms.Api.Features.Branches;

/// <summary>Branch snapshot and copy (FR-ORG-06).</summary>
public static class BranchesModule
{
    public static IServiceCollection AddBranches(this IServiceCollection services) => services.AddBranchServices();

    public static IEndpointRouteBuilder MapBranchEndpoints(this IEndpointRouteBuilder app)
    {
        CopyBranchEndpoint.Map(app);
        return app;
    }
}
