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
