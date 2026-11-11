using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lms.Infrastructure.Identity.External;

public static class ExternalIdentityServiceCollectionExtensions
{
    /// <summary>
    /// Data Protection with the key ring in MySQL (shared by all nodes), the secret protector, and the dynamic
    /// OpenID Connect scheme registry (schemes are added at runtime, so the handler's option plumbing is registered here).
    /// </summary>
    public static IServiceCollection AddExternalIdentity(this IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName("Lms");
        services.AddOptions<KeyManagementOptions>()
            .Configure<IServiceScopeFactory>((options, scopes) => options.XmlRepository = new EfXmlRepository(scopes));
        services.AddSingleton<ProviderSecretProtector>();
        services.AddScoped<ExternalLoginLinker>();

        services.AddSingleton<ExternalSchemeRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<OpenIdConnectOptions>, ExternalOidcOptionsSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>());
        services.AddHostedService<ExternalSchemeStartup>();
        return services;
    }

    /// <summary>Registers the schemes of the stored providers when the host starts.</summary>
    private sealed class ExternalSchemeStartup(ExternalSchemeRegistry registry, ILogger<ExternalSchemeStartup> logger) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await registry.SyncAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A database that is not migrated yet must not stop the host; the next save or restart registers them.
                logger.LogWarning(ex, "External identity schemes could not be registered at startup");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
