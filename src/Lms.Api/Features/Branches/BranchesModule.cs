using Lms.Api.Infrastructure;
using Lms.Infrastructure.Branches;

namespace Lms.Api.Features.Branches;

/// <summary>Branch snapshot and copy (FR-ORG-06).</summary>
public sealed class BranchesModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddBranchServices();

    public void MapEndpoints(IEndpointRouteBuilder app) => CopyBranchEndpoint.Map(app);
}
