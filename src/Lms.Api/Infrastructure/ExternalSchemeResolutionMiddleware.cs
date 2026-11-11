using Lms.Infrastructure.Identity.External;

namespace Lms.Api.Infrastructure;

/// <summary>
/// The provider's redirect back (<c>/signin-oidc/{slug}</c>) is handled by that provider's authentication scheme, which a node
/// only knows after it has synced the table. When the sign-in started on another node, or a provider was saved after this node
/// started, the scheme is registered here on demand, before authentication looks for a handler for the path.
/// </summary>
public sealed class ExternalSchemeResolutionMiddleware(RequestDelegate next)
{
    public const string CallbackPrefix = "/signin-oidc/";

    public async Task InvokeAsync(HttpContext http, ExternalSchemeRegistry registry, ProviderLookup providers)
    {
        var path = http.Request.Path.Value;
        if (path is not null && path.StartsWith(CallbackPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var slug = path[CallbackPrefix.Length..].Trim('/');
            if (slug.Length is > 0 and <= 40 && !registry.HasSlug(slug)
                && await providers.FindBySlugAsync(slug, http.RequestAborted) is not null)
            {
                await registry.SyncAsync(http.RequestAborted);
            }
        }

        await next(http);
    }
}
