using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lms.Infrastructure.Seeding;

public static class SeedingServiceCollectionExtensions
{
    public static IServiceCollection AddDevSeeding(this IServiceCollection services)
    {
        services.AddSingleton(sp => new DevSeeder(
            sp.GetRequiredService<IConfiguration>().GetConnectionString("Lms")
                ?? throw new InvalidOperationException("ConnectionStrings:Lms is not configured."),
            sp.GetRequiredService<IHostEnvironment>(),
            new SeedOptions(
                sp.GetRequiredService<IConfiguration>()["Seed:PlatformAdminEmail"],
                sp.GetRequiredService<IConfiguration>()["Seed:DefaultPassword"],
                int.TryParse(sp.GetRequiredService<IConfiguration>()["Seed:Perf:Students"], out var perf) ? perf : 0)));
        return services;
    }
}