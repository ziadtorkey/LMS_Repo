using Lms.Shared.Authorization;
using MudBlazor;

namespace Lms.Client.Navigation;

/// <summary>All navigation entries; later milestones append items here.</summary>
public static class NavCatalog
{
    public static readonly IReadOnlyList<NavItem> Items =
    [
        new("Nav_Home", "", Icons.Material.Filled.Home, null),
        new("Nav_OrgUnits", "org-units", Icons.Material.Filled.AccountTree, Permissions.OrgUnits.View),
        new("Nav_LevelTypes", "level-types", Icons.Material.Filled.Layers, Permissions.LevelTypes.View),
        new("Nav_Roles", "roles", Icons.Material.Filled.AdminPanelSettings, Permissions.Roles.View),
        new("Nav_Settings", "settings", Icons.Material.Filled.Settings, Permissions.Settings.View),
        new("Nav_Audit", "audit", Icons.Material.Filled.History, Permissions.Audit.View),
        new("Nav_Clients", "clients", Icons.Material.Filled.Business, null, PlatformAdminOnly: true),
    ];
}
