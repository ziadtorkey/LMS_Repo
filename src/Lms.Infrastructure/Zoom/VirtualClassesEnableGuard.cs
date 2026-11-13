using Lms.Domain.Configuration;
using Lms.Domain.LiveSessions;
using Lms.Domain.Organizations;
using Lms.Shared;
using Lms.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace Lms.Infrastructure.Zoom;

/// <summary>
/// Turning <c>Features.VirtualClasses</c> on needs a working provider (D49, D53): Zoom configured and accepting the credentials, and
/// every pool host existing and licensed in the Zoom account. Otherwise the change is refused with 409 <c>provider_not_configured</c>
/// and nothing is stored. A provider other than Zoom (the Development fake) is judged by its own health only. Turning the flag off
/// is never refused.
/// </summary>
public sealed class VirtualClassesEnableGuard(IMeetingProvider provider, ZoomApiClient zoom, IOptions<ZoomOptions> options) : ISettingChangeGuard
{
    public string SettingName => FeatureFlags.VirtualClasses.Name;

    public async Task<string?> RefuseAsync(OrgUnit unit, string valueJson, CancellationToken ct)
    {
        if (valueJson.Trim() != "true")
        {
            return null;
        }

        var health = await provider.GetHealthAsync(ct);
        if (!health.Configured || !health.Healthy)
        {
            return ProblemCodes.provider_not_configured;
        }

        if (provider is not ZoomMeetingProvider)
        {
            return null;
        }

        try
        {
            foreach (var host in options.Value.EffectiveHostPool)
            {
                if (!await zoom.IsLicensedHostAsync(host, ct))
                {
                    return ProblemCodes.provider_not_configured; // a pool user that does not exist or holds no licence cannot host
                }
            }
        }
        catch (ZoomProviderException)
        {
            return ProblemCodes.provider_not_configured; // Zoom could not answer: do not switch the feature on blind
        }

        return null;
    }
}
