using Lms.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Statements per round trip. Measured on the 500-students-x-12-courses placement (X-09): 4.6 s with the provider
    /// default, 3.4 s with 100; 300 and above make the provider fail ("CommandText not initialized"), so stay at 100.
    /// </summary>
    private const int MaxBatchSize = 100;

    public static DbContextOptionsBuilder UseLmsMySql(this DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseMySQL(connectionString, o => o.MaxBatchSize(MaxBatchSize)).UseOpenIddict<Guid>();

    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Lms")
            ?? throw new InvalidOperationException("ConnectionStrings:Lms is not configured.");

        services.AddScoped<StampingInterceptor>();
        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<LmsDbContext>((sp, options) =>
        {
            options.UseLmsMySql(connectionString);
            // Order matters: stamping first, audit (added by the audit module) after it.
            options.AddInterceptors(
                sp.GetRequiredService<StampingInterceptor>(),
                sp.GetRequiredService<AuditInterceptor>());
        });

        return services;
    }
}