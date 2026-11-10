using Lms.Domain.Abstractions;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Auditing;
using Lms.Infrastructure.Files;
using Lms.Infrastructure.Identity;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Single entry point; each module adds one line here that calls its own Add&lt;Module&gt; extension.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddPersistence(configuration);
        services.AddAccess();
        services.AddOrganizations();
        services.AddAuditing();
        services.AddFileStorage(configuration);
        services.AddLmsIdentityStores();
        services.AddSettingsAndPolicies();
        return services;
    }
}
