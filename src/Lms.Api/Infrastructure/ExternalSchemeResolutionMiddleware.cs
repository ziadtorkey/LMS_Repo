using Lms.Infrastructure.Identity.External;
using Microsoft.Extensions.Caching.Memory;

namespace Lms.Api.Infrastructure;

/// <summary>
/// The provider's redirect back (<c>/signin-oidc/{slug}</c>) is handled by that provider's authentication scheme, which a node
/// only knows after it has synced the table. When the sign-in started on another node, or a provider was saved after this node
/// started, the scheme is registered here on demand, before authentication looks for a handler for the path.
/// </summary>
public sealed class ExternalSchemeResolutionMiddleware(RequestDelegate next, IMemoryCache unknown)
{
    public const string CallbackPrefix = "/signin-oidc/";
    private const string UnknownKey = "ext-unknown-slug:";

    public async Task InvokeAsync(HttpContext http, ExternalSchemeRegistry registry, ProviderLookup providers)
    {
        var path = http.Request.Path.Value;
        if (path is not null && path.StartsWith(CallbackPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var slug = path[CallbackPrefix.Length..].Trim('/');
            // Unknown slugs are remembered for a short while, so requests for made-up callback paths cost one lookup, not one each.
            if (slug.Length is > 0 and <= 40 && !registry.HasSlug(slug) && !unknown.TryGetValue(UnknownKey + slug, out _))
            {
                if (await providers.FindBySlugAsync(slug, http.RequestAborted) is not null)
                {
                    await registry.SyncAsync(http.RequestAborted);
                }
                else
                {
                    unknown.Set(UnknownKey + slug, true, TimeSpan.FromSeconds(30));
                }
            }
        }

        await next(http);
    }
}
