using Lms.Domain.IdentityProviders;
using Lms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lms.Infrastructure.Identity.External;

/// <summary>What the OpenID Connect options of one registered provider are built from (secret already decrypted).</summary>
public sealed record ExternalProviderSnapshot(
    Guid Id, Guid OrganizationId, string Slug, string DisplayName, string Authority, string ClientId, string? ClientSecret, string Scopes);

/// <summary>
/// Keeps one authentication scheme per enabled provider (<c>ext-{id:N}</c>) in step with the table: at startup and after
/// every save or delete. Providers are configured at runtime by client admins, so schemes cannot be declared in code.
/// </summary>
public sealed class ExternalSchemeRegistry(
    IServiceScopeFactory scopes,
    IAuthenticationSchemeProvider schemes,
    IOptionsMonitorCache<OpenIdConnectOptions> optionsCache,
    ProviderSecretProtector protector,
    ILogger<ExternalSchemeRegistry> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile IReadOnlyDictionary<string, ExternalProviderSnapshot> _snapshots =
        new Dictionary<string, ExternalProviderSnapshot>();

    public static string SchemeName(Guid providerId) => "ext-" + providerId.ToString("N");

    public ExternalProviderSnapshot? Find(string schemeName) => _snapshots.GetValueOrDefault(schemeName);

    public async Task SyncAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            List<IdentityProviderConfig> rows;
            using (var scope = scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
                // ISOLATION-BYPASS: schemes are process-wide and exist before any caller; reads enabled, live rows of every client.
                rows = await db.Set<IdentityProviderConfig>().IgnoreQueryFilters()
                    .Where(p => p.Enabled && !p.IsDeleted).AsNoTracking().ToListAsync(ct);
            }

            var next = rows.ToDictionary(
                p => SchemeName(p.Id),
                p => new ExternalProviderSnapshot(
                    p.Id, p.OrganizationId, p.Slug, p.DisplayNameEn, p.Authority, p.ClientId,
                    p.ProtectedClientSecret is null ? null : protector.Unprotect(p.ProtectedClientSecret), p.Scopes));

            foreach (var gone in _snapshots.Keys.Where(name => !next.ContainsKey(name)))
            {
                schemes.RemoveScheme(gone);
                optionsCache.TryRemove(gone);
            }

            foreach (var (name, _) in next)
            {
                // Changed settings must not be served from cached options.
                optionsCache.TryRemove(name);
                if (schemes.GetSchemeAsync(name).GetAwaiter().GetResult() is null)
                {
                    schemes.AddScheme(new AuthenticationScheme(name, next[name].DisplayName, typeof(OpenIdConnectHandler)));
                }
            }

            _snapshots = next;
            logger.LogInformation("External identity schemes synced: {Count} enabled", next.Count);
        }
        finally
        {
            _gate.Release();
        }
    }
}
