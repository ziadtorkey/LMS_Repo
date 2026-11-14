using Lms.Infrastructure.Packages;
using Microsoft.Extensions.Options;

namespace Lms.Api.PackageHost;

/// <summary>
/// The headers of every package-host response, errors included (ADR 0005 section 2, plan Decision 12): a strict CSP whose
/// <c>sandbox</c> directive is <c>allow-scripts</c> only, nosniff, no referrer, no caching, CORS <c>*</c> without credentials for
/// GET/HEAD/OPTIONS, and never a cookie, <c>X-Frame-Options</c> or CORP/COEP/COOP header. Also the last safety net: an unhandled
/// exception answers the same 404 as every other failure and is logged without its message or path (they can carry the token).
/// </summary>
public sealed class PackageHostHeadersMiddleware(RequestDelegate next, IOptions<PackageHostOptions> options, ILogger<PackageHostHeadersMiddleware> logger)
{
    private readonly string _csp =
        "sandbox allow-scripts; default-src 'none'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; media-src 'self' blob:; font-src 'self' data:; connect-src 'self'; frame-src 'none'; " +
        $"form-action 'none'; base-uri 'none'; frame-ancestors {PackageTokenService.Normalise(options.Value.AppOrigin)}";

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.Remove("Set-Cookie");
            headers.Remove("X-Frame-Options");
            headers.Remove("Cross-Origin-Resource-Policy");
            headers.Remove("Cross-Origin-Embedder-Policy");
            headers.Remove("Cross-Origin-Opener-Policy");
            headers["Content-Security-Policy"] = _csp;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cache-Control"] = "private, no-store";
            headers["Access-Control-Allow-Origin"] = "*";
            headers["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS";
            return Task.CompletedTask;
        });

        if (HttpMethods.IsOptions(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent; // CORS preflight: the answer is the headers above
            return;
        }

        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound; // the one answer of every failure, not 405
            return;
        }

        try
        {
            await next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            logger.LogError("Package host request failed ({ExceptionType})", ex.GetType().Name);
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }
}
