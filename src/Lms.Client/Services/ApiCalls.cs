using Lms.Client.Services.Api;
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
