namespace Lms.Client.Navigation;

/// <summary>One entry of the permission-driven side navigation.</summary>
public sealed record NavItem(string ResourceKey, string Href, string Icon, string? RequiredPermission, bool PlatformAdminOnly = false)
{
    /// <summary>Stable id used for data-testid="nav-&lt;Key&gt;" (e.g. Nav_OrgUnits → orgunits).</summary>
    public string Key => ResourceKey.StartsWith("Nav_", StringComparison.Ordinal)
        ? ResourceKey[4..].ToLowerInvariant()
        : ResourceKey.ToLowerInvariant();
}
