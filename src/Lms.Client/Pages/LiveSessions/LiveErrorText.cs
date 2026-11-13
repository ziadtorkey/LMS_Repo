using Lms.Client.Resources;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Shared;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Pages.LiveSessions;

/// <summary>Plain-words messages for the live-session errors (design spec M4b §2.3).</summary>
public static class LiveErrorText
{
    /// <summary>
    /// <c>provider_unavailable</c> says how long to wait: the <c>Retry-After</c> seconds rounded up to minutes ("in about 5 min"),
    /// "in a minute" below a minute or without the header. <c>feature_disabled</c> is the plain "turned off" message.
    /// </summary>
    /// <summary>
    /// <see cref="ApiCalls.RunAsync(Func{Task}, Microsoft.AspNetCore.Components.NavigationManager, IStringLocalizer{SharedResource}, Action{string})"/>
    /// for live-session calls: the same redirects, but a problem is worded by <see cref="For"/> so <c>provider_unavailable</c> keeps its
    /// <c>Retry-After</c> and <c>feature_disabled</c> its plain message.
    /// </summary>
    public static async Task<bool> RunAsync(Func<Task> action, Microsoft.AspNetCore.Components.NavigationManager navigation,
        IStringLocalizer<SharedResource> l, bool arabic, Action<string> onError)
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
            onError(For(ex, l, arabic));
            return false;
        }
        catch (HttpRequestException)
        {
            onError(l["Error_Generic"].Value);
            return false;
        }
    }

    public static string For(ApiProblemException ex, IStringLocalizer<SharedResource> l, bool arabic)
    {
        if (ex.Code == ProblemCodes.feature_disabled) return l["Live_FeatureOff"];
        if (ex.Code == ProblemCodes.provider_unavailable)
        {
            if (ex.RetryAfterSeconds is { } s && s > 60)
            {
                var minutes = (int)Math.Ceiling(s / 60.0);
                return l["Live_Error_Busy", l.Count("Live_Unit_Minute", minutes, arabic)];
            }
            return l["Live_Error_BusyShort"];
        }
        return ProblemText.For(ex, l);
    }
}
