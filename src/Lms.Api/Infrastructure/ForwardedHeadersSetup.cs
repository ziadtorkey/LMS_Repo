using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Lms.Api.Infrastructure;

/// <summary>
/// Behind a reverse proxy the peer address is the proxy's, which would put every user into one rate-limit bucket and
/// into every audit entry. X-Forwarded-For / X-Forwarded-Proto are honoured only from the proxies listed in
/// ForwardedHeaders:KnownProxies (addresses) and ForwardedHeaders:KnownNetworks (CIDR, e.g. 10.0.0.0/8). With neither
/// configured nothing is trusted, so a client cannot fake its address by sending the header itself.
/// </summary>
public static class ForwardedHeadersSetup
{
    public static IServiceCollection AddLmsForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").GetChildren()
            .Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => IPAddress.Parse(v!)).ToList();
        var networks = configuration.GetSection("ForwardedHeaders:KnownNetworks").GetChildren()
            .Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => System.Net.IPNetwork.Parse(v!)).ToList();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // The defaults trust loopback; "trust nothing unless configured" means starting from empty lists.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in proxies)
            {
                options.KnownProxies.Add(proxy);
            }

            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(network);
            }

            // One proxy hop; with the default limit of 1 only the entry added by the trusted proxy is used.
            options.ForwardLimit = 1;
        });

        return services;
    }

    /// <summary>
    /// Adds the middleware only when at least one proxy or network is configured: the framework treats empty lists as
    /// "trust every sender", the opposite of the default wanted here.
    /// </summary>
    public static WebApplication UseLmsForwardedHeaders(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ForwardedHeadersOptions>>().Value;
        if (options.KnownProxies.Count > 0 || options.KnownIPNetworks.Count > 0)
        {
            app.UseForwardedHeaders();
        }

        return app;
    }
}
