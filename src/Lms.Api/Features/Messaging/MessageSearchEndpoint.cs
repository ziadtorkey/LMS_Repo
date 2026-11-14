using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Messaging;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;

namespace Lms.Api.Features.Messaging;

/// <summary>GET <c>/api/messages/search</c> (L21): only the caller own conversations, at least two characters, 50 per page inside a 200-hit window, rate limited, never cached.</summary>
public static class MessageSearchEndpoint
{
    public static IEndpointRouteBuilder MapMessageSearch(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.MessageSearch, SearchAsync).RequirePermission(Permissions.Messaging.View);
        return app;
    }

    private static async Task<IResult> SearchAsync(
        string? q, Guid? conversationId, HttpContext http, ICurrentContext context, ISettingsResolver settings, MessageSearch search, MessageSearchThrottle throttle, CancellationToken ct, int page = 1)
    {
        if (await ConversationEndpoints.GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        var text = q?.Trim() ?? string.Empty;
        if (text.Length < MessagingLimits.SearchMinLength || text.Length > 100 || page < 1)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "A search needs at least two characters.");
        }

        if (throttle.TryTake(context.UserId!.Value) is { } wait)
        {
            http.Response.Headers.RetryAfter = wait.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many requests", extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.rate_limited });
        }

        return Results.Ok(await search.SearchAsync(text, conversationId, page, ct));
    }
}
