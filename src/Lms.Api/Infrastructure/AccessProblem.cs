using Lms.Domain.Access;
using Lms.Shared.Authorization;
using Microsoft.Net.Http.Headers;

namespace Lms.Api.Infrastructure;

/// <summary>The 401/403 body for a denied <see cref="AccessDecision"/>: reason, blocking unit and localized policy message.</summary>
public static class AccessProblem
{
    public static IResult From(AccessDecision decision, HttpContext http)
    {
        var extensions = new Dictionary<string, object?> { ["reason"] = decision.Reason.ToString() };
        if (decision.BlockingOrgUnitId is { } blocking)
        {
            extensions["blockingOrgUnitId"] = blocking;
        }

        return decision.Reason == AccessDenialReason.NotAuthenticated
            ? Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", extensions: extensions)
            : Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: decision.Reason == AccessDenialReason.BlockedByPolicy ? PolicyMessage(http) : null,
                extensions: extensions);
    }

    /// <summary>Arabic is the default UI culture; English only when the client prefers it.</summary>
    private static string PolicyMessage(HttpContext http)
    {
        var preferred = http.Request.GetTypedHeaders().AcceptLanguage
            .OrderByDescending(l => l.Quality ?? 1)
            .Select(l => l.Value.Value ?? string.Empty)
            .FirstOrDefault(v => v.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
                                 || v.StartsWith("en", StringComparison.OrdinalIgnoreCase));

        return preferred is not null && preferred.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? PolicyCatalog.BlockedMessageEn
            : PolicyCatalog.BlockedMessageAr;
    }
}