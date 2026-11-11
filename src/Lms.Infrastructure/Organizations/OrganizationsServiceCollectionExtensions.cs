using Lms.Domain.Organizations;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Organizations;

public static class OrganizationsServiceCollectionExtensions
{
    public static IServiceCollection AddOrganizations(this IServiceCollection services)
    {
        services.AddScoped<IOrgTree, OrgTree>();
        services.AddScoped<AssignmentUsageCheck>();
        services.AddScoped<IOrgUnitRepather, OrgUnitRepather>();
        services.AddScoped<IOrgPolicyReader, OrgPolicyReader>();
        return services;
    }
}
