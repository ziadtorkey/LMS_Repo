using Lms.Client.Resources;
using Lms.Client.Services.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Services;

/// <summary>
/// Runs a user action (save, delete, export) so no API failure reaches Blazor's global error bar: stale context,
/// forbidden page and token problems redirect (same rules as the server tables), problem codes and network errors
/// become a message for the caller to show.
/// </summary>
public static class ActionCalls
{
    /// <returns>true when the action succeeded; false when it failed or the user was redirected.</returns>
    public static async Task<bool> TryAsync(
        Func<Task> action, NavigationManager navigation, IStringLocalizer<SharedResource> l, Action<string> showError)
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception ex) when (ApiCalls.TryRedirect(ex, navigation))
        {
            return false;
        }
        catch (ApiProblemException ex)
        {
            showError(ProblemText.For(ex, l));
        }
        catch (HttpRequestException)
        {
            showError(l["Api_Error"]);
        }
        return false;
    }
}
