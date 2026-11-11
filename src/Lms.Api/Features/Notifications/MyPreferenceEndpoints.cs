using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Infrastructure.Notifications;
using Lms.Shared;
using Lms.Shared.Notifications;

namespace Lms.Api.Features.Notifications;

/// <summary>
/// The caller's own notification channel preferences. No permission: they only ever read and write the rows of the
/// caller (UserId from the token), so authentication is the whole gate.
/// </summary>
public static class MyPreferenceEndpoints
{
    public static IEndpointRouteBuilder MapMyPreferenceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.MyNotificationPreferences, ListAsync).RequireAuthorization();
        app.MapPut(Routes.MyNotificationPreferences, ReplaceAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> ListAsync(NotificationPreferences preferences, ICurrentContext context, CancellationToken ct) =>
        context.UserId is { } userId
            ? Results.Ok(await preferences.ListAsync(userId, ct))
            : Results.Unauthorized();

    private static async Task<IResult> ReplaceAsync(
        SetNotificationPreferencesRequest? body, NotificationPreferences preferences, ICurrentContext context, CancellationToken ct)
    {
        if (context.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var errors = new Dictionary<string, string[]>();
        var list = body?.Preferences;
        if (list is null)
        {
            errors["preferences"] = ["preferences_required"];
        }
        else if (list.Any(p => p is null))
        {
            // A JSON null item would otherwise fail below with a 500.
            errors["preferences"] = ["preference_required"];
        }
        else
        {
            if (list.Any(p => p.Channel == NotificationChannels.InApp && !p.Enabled))
            {
                return ApiProblems.BadRequest(ProblemCodes.in_app_required);
            }

            if (list.Any(p => NotificationEvents.Find(p.EventKey) is null))
            {
                errors["eventKey"] = ["unknown_event"];
            }

            if (list.Any(p => !NotificationChannels.All.Contains(p.Channel, StringComparer.Ordinal)))
            {
                errors["channel"] = ["unknown_channel"];
            }

            if (list.GroupBy(p => (p.EventKey, p.Channel)).Any(g => g.Count() > 1))
            {
                errors["preferences"] = ["duplicate_preference"];
            }
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(
                errors, extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });
        }

        await preferences.ReplaceAsync(userId, list!.Select(p => (p.EventKey, p.Channel, p.Enabled)), ct);
        return Results.NoContent();
    }
}
