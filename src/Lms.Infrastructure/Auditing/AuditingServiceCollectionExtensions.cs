using Lms.Domain.Auditing;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Auditing;

public static class AuditingServiceCollectionExtensions
{
    public static IServiceCollection AddAuditing(this IServiceCollection services)
    {
        services.AddScoped<IAuditWriter, AuditWriter>();
        return services;
    }
}
