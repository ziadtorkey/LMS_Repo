using Lms.Shared.Notifications;
using Microsoft.Extensions.Primitives;

namespace Lms.Api.Features.Notifications;

/// <summary>
/// Browsers cannot set an Authorization header on a WebSocket, so SignalR sends the token as ?access_token=. For the
/// hub path only (never for the API), the query value is moved into the Authorization header before authentication runs.
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

                return nextMiddleware(context);
            });
            next(app);
        };
}
