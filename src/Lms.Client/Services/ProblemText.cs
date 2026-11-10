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
        if (!string.IsNullOrEmpty(ex.Code))
        {
            var specific = l[$"Error_{ex.Code}"];
            if (!specific.ResourceNotFound) return specific.Value;
        }
        return l["Error_Generic"].Value;
    }
}
