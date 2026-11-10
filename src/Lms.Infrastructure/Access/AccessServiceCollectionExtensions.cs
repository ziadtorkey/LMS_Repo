using Lms.Domain.Access;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Access;

public static class AccessServiceCollectionExtensions
{
    public static IServiceCollection AddAccess(this IServiceCollection services)
    {
        services.AddScoped<IDefaultRoleProvisioner, DefaultRoleProvisioner>();
        return services;
    }
}