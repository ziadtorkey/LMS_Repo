using System.Globalization;
using Lms.Shared.Organizations;
using MudBlazor;

namespace Lms.Client.Pages.Organization;

/// <summary>A tree node; <see cref="IsMatch"/> marks nodes that match the active search (ancestors are only expanded).</summary>
public sealed class OrgTreeItem : TreeItemData<OrgUnitNodeDto>
{
    public bool IsMatch { get; init; }
}

public static class OrgTreeBuilder
{
    /// <summary>
    /// Turns the flat server list into a nested tree. Siblings are sorted by the name shown in the current culture.
    /// With a search term every node that has children is expanded (the server returns matches plus their ancestors)
    /// and matches are flagged; otherwise roots and the nodes the user expanded stay open.
    /// </summary>
    public static IReadOnlyList<TreeItemData<OrgUnitNodeDto>> Build(
        IReadOnlyList<OrgUnitNodeDto> units, Func<OrgUnitNodeDto, string> displayName, string? search, IReadOnlyCollection<Guid>? expanded)
    {
        var ids = units.Select(u => u.Id).ToHashSet();
        var searching = !string.IsNullOrWhiteSpace(search);
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        var childrenOf = units.Where(u => u.ParentId is { } p && ids.Contains(p))
            .GroupBy(u => u.ParentId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(displayName, comparer).ToList());

        bool Matches(OrgUnitNodeDto u) => searching &&
            (u.NameAr.Contains(search!, StringComparison.OrdinalIgnoreCase) ||
             u.NameEn.Contains(search!, StringComparison.OrdinalIgnoreCase) ||
             u.Code.Contains(search!, StringComparison.OrdinalIgnoreCase));

        TreeItemData<OrgUnitNodeDto> ToItem(OrgUnitNodeDto unit, bool isRoot)
        {
            var kids = childrenOf.TryGetValue(unit.Id, out var list)
                ? list.Select(c => ToItem(c, false)).ToList()
                : [];
            return new OrgTreeItem
            {
                Value = unit,
                Text = displayName(unit),
                IsMatch = Matches(unit),
                Expandable = kids.Count > 0,
                Expanded = kids.Count > 0 && (searching || isRoot || (expanded?.Contains(unit.Id) ?? false)),
                Children = kids,
            };
        }

        return units.Where(u => u.ParentId is not { } p || !ids.Contains(p))
            .OrderBy(displayName, comparer)
            .Select(u => ToItem(u, true))
            .ToList();
    }

    /// <summary>Tree icon by depth (Stitch organization tree): the root institution, campuses or schools, then divisions and levels.</summary>
    public static string DepthIcon(int depth) => depth switch
    {
        0 => MudBlazor.Icons.Material.Rounded.AccountBalance,
        1 => MudBlazor.Icons.Material.Rounded.Domain,
        _ => MudBlazor.Icons.Material.Rounded.Layers,
    };
}
