using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Branches;

public static class BranchesServiceCollectionExtensions
{
    public static IServiceCollection AddBranchServices(this IServiceCollection services)
    {
        services.AddScoped<IBranchSnapshotter, BranchSnapshotter>();
        services.AddScoped<IBranchMaterializer, BranchMaterializer>();
        return services;
    }
}
