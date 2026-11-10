using Lms.Shared.Authorization;
using Lms.Shared.Notifications;
using Microsoft.Extensions.Primitives;

namespace Lms.Api.Features.Notifications;

/// <summary>
/// Browsers cannot set an Authorization header on a WebSocket, so SignalR sends the token as ?access_token=. For the
/// hub path only (never for the API), the query value is moved into the Authorization header before authentication runs.
/// The active context travels the same way (?context=).
/// </summary>
public sealed class HubAccessTokenStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Path.StartsWithSegments(NotificationHub.Path, StringComparison.OrdinalIgnoreCase)
                    && !context.Request.Headers.ContainsKey("Authorization")
                    && context.Request.Query.TryGetValue("access_token", out var token)
                    && !StringValues.IsNullOrEmpty(token))
                {
                    context.Request.Headers.Authorization = "Bearer " + token;
                }

                // Same for the active context: ?context=assignmentId becomes the X-Lms-Context header, so the one
                // middleware that validates contexts also covers hub connections.
                if (context.Request.Path.StartsWithSegments(NotificationHub.Path, StringComparison.OrdinalIgnoreCase)
                    && !context.Request.Headers.ContainsKey(ContextHeader.Name)
                    && context.Request.Query.TryGetValue("context", out var assignment)
                    && !StringValues.IsNullOrEmpty(assignment))
                {
                    context.Request.Headers[ContextHeader.Name] = assignment;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
}
