namespace Lms.Client.Navigation;

/// <summary>
/// One entry of the permission-driven side navigation. <c>Section</c> is the sidebar group it sits under (a NavSection_* resource key,
/// Stitch grouped navigation, #561); null keeps the entry above the groups (Home).
/// </summary>
public sealed record NavItem(string ResourceKey, string Href, string Icon, string? RequiredPermission, bool PlatformAdminOnly = false, string? Section = null)
{
    /// <summary>Stable id used for data-testid="nav-&lt;Key&gt;" (e.g. Nav_OrgUnits → orgunits).</summary>
    public string Key => ResourceKey.StartsWith("Nav_", StringComparison.Ordinal)
        ? ResourceKey[4..].ToLowerInvariant()
        : ResourceKey.ToLowerInvariant();
}
