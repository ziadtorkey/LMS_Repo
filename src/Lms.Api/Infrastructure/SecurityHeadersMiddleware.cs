namespace Lms.Api.Infrastructure;

/// <summary>Adds the baseline security headers to every response (clickjacking, MIME sniffing, referrer leakage).</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Content-Security-Policy"] = "frame-ancestors 'none'";
            return Task.CompletedTask;
        });

        return next(context);
    }
}
