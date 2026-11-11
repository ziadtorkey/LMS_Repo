using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Identity.External;

public static class ExternalIdentityServiceCollectionExtensions
{
    /// <summary>Data Protection with the key ring in MySQL (shared by all nodes) and the secret protector for provider configuration.</summary>
    public static IServiceCollection AddExternalIdentity(this IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName("Lms");
        services.AddOptions<KeyManagementOptions>()
            .Configure<IServiceScopeFactory>((options, scopes) => options.XmlRepository = new EfXmlRepository(scopes));
        services.AddSingleton<ProviderSecretProtector>();
        return services;
    }
}
