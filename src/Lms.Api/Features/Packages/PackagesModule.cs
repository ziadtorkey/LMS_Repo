using Lms.Api.Infrastructure;
using Lms.Domain.Packages;
using Lms.Infrastructure.Packages;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lms.Api.Features.Packages;

/// <summary>SCORM and H5P packages: upload, status, attach (M4b Task 8a: FR-STD-01, FR-STD-02, X-05, D51, D56). Launch, preview and tracking are separate tasks.</summary>
public sealed class PackagesModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.TryAddScoped<IPackageGradeRecompute, NoOpPackageGradeRecompute>(); // replaced by the attempts task (T9b)

    public void MapEndpoints(IEndpointRouteBuilder app) => PackageEndpoints.Map(app);
}
