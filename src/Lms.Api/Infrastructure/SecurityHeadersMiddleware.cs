using Lms.Infrastructure.Packages;
using Microsoft.Extensions.Options;

namespace Lms.Api.Infrastructure;

/// <summary>
/// Adds the baseline security headers to every response (clickjacking, MIME sniffing, referrer leakage). When the package content host is
/// configured the app policy adds exactly one thing: <c>frame-src &lt;content origin&gt;</c> (ADR 0005 section 3), so a package cannot
/// navigate its own frame to an outside URL.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IOptions<PackageHostOptions> packageHost)
{
    private readonly string _csp = packageHost.Value.IsConfigured
        ? $"frame-ancestors 'none'; frame-src {new Uri(packageHost.Value.Origin!).GetLeftPart(UriPartial.Authority)}"
        : "frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Content-Security-Policy"] = _csp;
            return Task.CompletedTask;
        });

        return next(context);
    }
}
