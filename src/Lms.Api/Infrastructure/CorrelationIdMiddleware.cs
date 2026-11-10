using System.Text.RegularExpressions;
using Serilog.Context;

namespace Lms.Api.Infrastructure;

/// <summary>Reads or creates X-Correlation-Id, echoes it and pushes it into the Serilog log context.</summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[HeaderName].FirstOrDefault();
        // Untrusted input ends up in logs and response headers: accept only a safe, short token.
        if (string.IsNullOrWhiteSpace(id) || !ValidId().IsMatch(id))
        {
            id = Guid.NewGuid().ToString("N");
        }

        context.Response.Headers[HeaderName] = id;
        using (LogContext.PushProperty("CorrelationId", id))
        {
            await next(context);
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z")]
    private static partial Regex ValidId();
}
