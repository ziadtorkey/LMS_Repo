using Lms.Client.Resources;
using Lms.Client.Services.Api;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Lms.Client.Services;

public static class ApiCalls
{
    /// <summary>
    /// Runs an API call so no exception escapes to the global error UI:
    /// stale/missing context → chooser, 403 → Forbidden page, missing token → login redirect, anything else → Failed = true.
    /// </summary>
    public static async Task<(T? Value, bool Failed)> RunAsync<T>(Func<Task<T>> call, NavigationManager navigation)
    {
        try
        {
            return (await call(), false);
        }
        catch (Exception ex) when (TryRedirect(ex, navigation))
        {
            return (default, false);
        }
        catch (HttpRequestException)
        {
            return (default, true);
        }
    }

    /// <summary>
    /// Runs a write/action handler with the standard failure handling, so no exception reaches the global error bar:
    /// redirects first (stale context → chooser, forbidden → Forbidden page, missing token → login), then
    /// <see cref="ApiProblemException"/> → <paramref name="onError"/> with <see cref="ProblemText"/>, then any other
    /// <see cref="HttpRequestException"/> → <paramref name="onError"/> with the generic message.
    /// Returns true on success; false when it failed or the user was redirected (<paramref name="onError"/> is then not called
    /// for redirects).
    /// </summary>
    public static async Task<bool> RunAsync(
        Func<Task> action, NavigationManager navigation, IStringLocalizer<SharedResource> localizer, Action<string> onError)
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception ex) when (TryRedirect(ex, navigation))
        {
            return false;
        }
        catch (ApiProblemException ex)
        {
            onError(ProblemText.For(ex, localizer));
            return false;
        }
        catch (HttpRequestException)
        {
            onError(localizer["Error_Generic"].Value);
            return false;
        }
    }

    /// <summary>
    /// Handles the "go somewhere else" failures (stale context, forbidden page, token) for callers that catch their own
    /// exceptions. Returns true when the exception was handled and the caller should show nothing.
    /// </summary>
    public static bool TryRedirect(Exception ex, NavigationManager navigation)
    {
        switch (ex)
        {
            case ApiContextException:
                navigation.NavigateTo("choose-context");
                return true;
            case ApiForbiddenException:
                navigation.NavigateTo("forbidden");
                return true;
            case AccessTokenNotAvailableException token:
                token.Redirect();
                return true;
            default:
                return false;
        }
    }
}
