using Lms.Domain.Access;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Access;

public static class AccessServiceCollectionExtensions
{
    public static IServiceCollection AddAccess(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<IDefaultRoleProvisioner, DefaultRoleProvisioner>();
        services.AddScoped<IAccessEvaluator, AccessEvaluator>();
        services.AddScoped<IRoleGrantGuard, RoleGrantGuard>();
        services.AddSingleton<IRolePermissionCache, RolePermissionCache>();
        services.AddScoped<CallerPermissions>();
        services.AddScoped<IRelationshipScope, RelationshipScope>();
        services.AddSingleton(sp => new BuiltInRoleStartupReconciler(
            sp.GetRequiredService<IConfiguration>().GetConnectionString("Lms")
                ?? throw new InvalidOperationException("ConnectionStrings:Lms is not configured."),
            sp.GetRequiredService<ILogger<BuiltInRoleStartupReconciler>>()));
        return services;
    }
}