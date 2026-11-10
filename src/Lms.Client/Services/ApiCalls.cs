using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Lms.Client.Services;

public static class ApiCalls
{
    /// <summary>
    /// Runs an API call so no exception escapes to the global error UI:
    /// 403 → Forbidden page, missing token → login redirect, anything else → Failed = true.
    /// </summary>
    public static async Task<(T? Value, bool Failed)> RunAsync<T>(Func<Task<T>> call, NavigationManager navigation)
    {
        try
        {
            return (await call(), false);
        }
        catch (ApiForbiddenException)
        {
            navigation.NavigateTo("forbidden");
            return (default, false);
        }
        catch (AccessTokenNotAvailableException ex)
        {
            ex.Redirect();
            return (default, false);
        }
        catch (HttpRequestException)
        {
            return (default, true);
        }
    }
}
