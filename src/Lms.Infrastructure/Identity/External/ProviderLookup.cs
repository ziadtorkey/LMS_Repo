using Lms.Domain.IdentityProviders;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Identity.External;

/// <summary>Finds the provider a not-yet-signed-in person should use. Read-only; only enabled providers of live, active clients.</summary>
public sealed class ProviderLookup(LmsDbContext db)
{
    public async Task<IdentityProviderConfig?> FindByEmailDomainAsync(string email, CancellationToken ct)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        if (at < 0 || at == email!.Length - 1)
        {
            return null;
        }

        var domain = email[(at + 1)..].Trim().ToLowerInvariant();
        if (domain.Length == 0 || domain.Contains(','))
        {
            return null;
        }

        var wrapped = "," + domain + ",";
        return await Enabled().FirstOrDefaultAsync(p => ("," + p.EmailDomains + ",").Contains(wrapped), ct);
    }

    public async Task<IdentityProviderConfig?> FindBySlugAsync(string slug, CancellationToken ct) =>
        await Enabled().FirstOrDefaultAsync(p => p.Slug == slug, ct);

    private IQueryable<IdentityProviderConfig> Enabled()
    {
        // ISOLATION-BYPASS: before sign-in there is no client; read-only, enabled live rows of active clients, re-applied explicitly.
        var providers = db.Set<IdentityProviderConfig>().IgnoreQueryFilters().Where(p => p.Enabled && !p.IsDeleted);
        // ISOLATION-BYPASS: same read; the client row itself is filtered explicitly so a suspended client has no sign-in.
        var activeClients = db.Organizations.IgnoreQueryFilters().Where(o => o.Status == OrganizationStatus.Active && !o.IsDeleted);
        return providers.Where(p => activeClients.Any(o => o.Id == p.OrganizationId));
    }
}
