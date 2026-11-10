using Lms.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static DbContextOptionsBuilder UseLmsMySql(this DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseMySQL(connectionString);

    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Lms")
            ?? throw new InvalidOperationException("ConnectionStrings:Lms is not configured.");

        services.AddScoped<StampingInterceptor>();
        services.AddDbContext<LmsDbContext>((sp, options) =>
        {
            options.UseLmsMySql(connectionString);
            // Order matters: stamping first, audit (added by the audit module) after it.
            options.AddInterceptors(sp.GetRequiredService<StampingInterceptor>());
        });

        return services;
    }
}