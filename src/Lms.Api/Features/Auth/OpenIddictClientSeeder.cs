using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Lms.Api.Features.Auth;

/// <summary>Registers (or updates) the public `lms-wasm` client for every configured app origin (Auth:WasmOrigins).</summary>
public sealed class OpenIddictClientSeeder(IServiceProvider services, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var origins = configuration.GetSection("Auth:WasmOrigins").Get<string[]>() ?? [];
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = AuthServiceCollectionExtensions.WasmClientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "LMS web app",
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Prefixes.Scope + Scopes.OpenId,
                Permissions.Prefixes.Scope + Scopes.OfflineAccess,
                Permissions.Prefixes.Scope + AuthServiceCollectionExtensions.ApiScope,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };

        foreach (var origin in origins.Select(o => o.TrimEnd('/')))
        {
            descriptor.RedirectUris.Add(new Uri($"{origin}/authentication/login-callback"));
            descriptor.PostLogoutRedirectUris.Add(new Uri($"{origin}/authentication/logout-callback"));
        }

        var existing = await manager.FindByClientIdAsync(descriptor.ClientId, cancellationToken);
        if (existing is null)
        {
            await manager.CreateAsync(descriptor, cancellationToken);
        }
        else
        {
            await manager.UpdateAsync(existing, descriptor, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
