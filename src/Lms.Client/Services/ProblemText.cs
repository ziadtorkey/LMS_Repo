using Lms.Client.Resources;
using Lms.Client.Services.Api;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Services;

public static class ProblemText
{
    /// <summary>User-facing message for an API problem: server detail for policy blocks, Error_{code} otherwise.</summary>
    public static string For(ApiProblemException ex, IStringLocalizer<SharedResource> l)
    {
        if (ex.Code == "BlockedByPolicy" && !string.IsNullOrWhiteSpace(ex.Detail))
            return ex.Detail;
        if (!string.IsNullOrEmpty(ex.Code) && !string.IsNullOrEmpty(ex.Field))
        {
            var byField = l[$"Error_{ex.Code}_{ex.Field}"];
            if (!byField.ResourceNotFound) return byField.Value;
        }
        if (!string.IsNullOrEmpty(ex.Code))
        {
            var specific = l[$"Error_{ex.Code}"];
            if (!specific.ResourceNotFound) return specific.Value;
        }
        return l["Error_Generic"].Value;
    }

    /// <summary>
    /// Like <see cref="For"/> for client-wide resources (level types, custom roles): an admin assigned below the
    /// client root gets OutOfScope, which is explained as "only at the top level".
    /// </summary>
    public static string ForClientWide(ApiProblemException ex, IStringLocalizer<SharedResource> l) =>
        ex.Code == "OutOfScope" ? l["LevelTypes_OutOfScope"].Value : For(ex, l);
}
