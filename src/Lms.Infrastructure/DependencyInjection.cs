using Lms.Domain.Abstractions;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.AcademicYears;
using Lms.Infrastructure.Auditing;
using Lms.Infrastructure.Files;
using Lms.Infrastructure.Email;
using Lms.Infrastructure.Identity;
using Lms.Infrastructure.Identity.External;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Organizations;
using Lms.Infrastructure.Notifications;
using Lms.Infrastructure.Persistence;
using Lms.Infrastructure.Seeding;
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
        services.AddDevSeeding();
        services.AddAuditing();
        services.AddNotificationCore();
        services.AddFileStorage(configuration);
        services.AddLmsIdentityStores();
        services.AddEmail(configuration);
        services.AddSettingsAndPolicies();
        services.AddAcademicYears();
        services.AddExternalIdentity(configuration);
        return services;
    }
}
