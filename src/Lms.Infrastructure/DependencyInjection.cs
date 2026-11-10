using Lms.Domain.Abstractions;
using Lms.Infrastructure.Auditing;
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
        services.AddAuditing();
        return services;
    }
}