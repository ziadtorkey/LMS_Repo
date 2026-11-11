using Lms.Api.Infrastructure;
using Lms.Shared.Authorization;

namespace Lms.Api.Features.Branches;

/// <summary>Which of the optional extras of a copied or applied branch the caller may write at the target.</summary>
public sealed record BranchExtras(bool Policies, bool Settings, IReadOnlyList<string> Skipped);

/// <summary>
/// Copying a branch or applying a template must not be a way around the permissions of the thing copied: policies and
/// settings are written at the target only when the caller could also set them there by hand (<c>Policies.Update</c> and
/// <c>Settings.Update</c> at the target); otherwise they are left out and reported. Shared by copy branch and template apply.
/// </summary>
public static class BranchExtrasPermission
{
    public static async Task<BranchExtras> ResolveAsync(
        HttpContext http, Guid targetParentId, bool wantPolicies, bool wantSettings, CancellationToken ct)
    {
        var skipped = new List<string>();
        var policies = wantPolicies;
        if (policies && (await http.CheckUnitAsync(Permissions.Policies.Update, targetParentId, ct)).Problem is not null)
        {
            policies = false;
            skipped.Add(CopyBranchEndpoint.PoliciesSkipped);
        }

        var settings = wantSettings;
        if (settings && (await http.CheckUnitAsync(Permissions.Settings.Update, targetParentId, ct)).Problem is not null)
        {
            settings = false;
            skipped.Add(CopyBranchEndpoint.SettingsSkipped);
        }

        return new BranchExtras(policies, settings, skipped);
    }
}
