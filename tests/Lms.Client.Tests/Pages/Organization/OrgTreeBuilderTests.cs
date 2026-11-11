using Lms.Client.Pages.Organization;
using Lms.Shared.Organizations;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Organization;

public sealed class OrgTreeBuilderTests
{
    private static OrgUnitNodeDto Node(Guid id, Guid? parent, string ar, string en, string code = "C", int depth = 0, bool children = false) =>
        new(id, parent, Guid.NewGuid(), ar, en, code, depth, "Active", false, null, false, children, []);

    private readonly Guid _root = Guid.NewGuid(), _a = Guid.NewGuid(), _b = Guid.NewGuid(), _a1 = Guid.NewGuid();

    private IReadOnlyList<OrgUnitNodeDto> Units() =>
    [
        Node(_a1, _a, "ألف-1", "Zeta child", depth: 2),
        Node(_b, _root, "باء", "Alpha", depth: 1),
        Node(_root, null, "الجذر", "Root", depth: 0, children: true),
        Node(_a, _root, "ألف", "Zulu", depth: 1, children: true),
    ];

    [Fact]
    public void Builder_nests_children_and_sorts_by_name_of_current_culture()
    {
        var english = OrgTreeBuilder.Build(Units(), n => n.NameEn, null, []);
        var arabic = OrgTreeBuilder.Build(Units(), n => n.NameAr, null, []);

        var root = Assert.Single(english);
        var rootKids = root.Children!.ToList();
        Assert.Equal(_root, root.Value!.Id);
        Assert.Equal(["Alpha", "Zulu"], rootKids.Select(c => c.Value!.NameEn));
        Assert.Equal(["ألف", "باء"], arabic[0].Children!.Select(c => c.Value!.NameAr));
        Assert.Equal(_a1, rootKids[1].Children!.Single().Value!.Id);
    }

    [Fact]
    public void Search_expands_ancestors_of_matches_and_flags_them()
    {
        var tree = OrgTreeBuilder.Build(Units(), n => n.NameEn, "zeta", []);

        var root = tree[0];
        var zulu = root.Children!.Single(c => c.Value!.Id == _a);
        var match = zulu.Children!.Single();
        Assert.True(root.Expanded);
        Assert.True(zulu.Expanded);
        Assert.True(((OrgTreeItem)match).IsMatch);
        Assert.False(((OrgTreeItem)zulu).IsMatch);
    }

    [Fact]
    public void Without_search_only_roots_and_remembered_nodes_are_expanded()
    {
        var tree = OrgTreeBuilder.Build(Units(), n => n.NameEn, null, [_a]);

        Assert.True(tree[0].Expanded);
        Assert.True(tree[0].Children!.Single(c => c.Value!.Id == _a).Expanded);
        Assert.False(tree[0].Children!.Single(c => c.Value!.Id == _b).Expanded);
    }

    [Fact]
    public void Units_whose_parent_is_not_visible_become_roots()
    {
        var tree = OrgTreeBuilder.Build([Node(_a, Guid.NewGuid(), "ألف", "Zulu")], n => n.NameEn, null, []);
        Assert.Single(tree);
    }
}
