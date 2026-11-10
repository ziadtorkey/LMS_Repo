using Lms.Domain.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Files;

public static class FilesServiceCollectionExtensions
{
    public static IServiceCollection AddFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(o =>
        {
            var section = configuration.GetSection("Storage");
            o.Endpoint = section["Endpoint"] ?? string.Empty;
            o.PublicEndpoint = section["PublicEndpoint"];
            o.AccessKey = section["AccessKey"] ?? string.Empty;
            o.SecretKey = section["SecretKey"] ?? string.Empty;
            o.Region = section["Region"] ?? o.Region;
            o.AllowedExtensions = section["AllowedExtensions"] ?? o.AllowedExtensions;
        });
        services.AddSingleton<MinioClients>();
        services.AddScoped<IFileStorage, MinioFileStorage>();
        return services;
    }
}
