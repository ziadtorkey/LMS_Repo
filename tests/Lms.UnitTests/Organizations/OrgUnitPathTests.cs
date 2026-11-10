using Lms.Domain.Organizations;

namespace Lms.UnitTests.Organizations;

public sealed class OrgUnitPathTests
{
    [Fact]
    public void Append_adds_the_id_as_N_formatted_segment()
    {
        var id = Guid.NewGuid();

        Assert.Equal("/a/" + id.ToString("N") + "/", OrgUnitPath.Append("/a/", id));
    }

    [Fact]
    public void AncestorsAndSelf_returns_every_prefix_from_the_root()
    {
        Assert.Equal(["/a/", "/a/b/", "/a/b/c/"], OrgUnitPath.AncestorsAndSelf("/a/b/c/"));
    }

    [Fact]
    public void IsWithin_is_true_for_descendant_and_self_and_false_for_ancestor()
    {
        Assert.True(OrgUnitPath.IsWithin("/a/b/", "/a/"));
        Assert.True(OrgUnitPath.IsWithin("/a/", "/a/"));
        Assert.False(OrgUnitPath.IsWithin("/a/", "/a/b/"));
    }

    [Fact]
    public void IsWithin_is_segment_safe_for_siblings_sharing_a_prefix()
    {
        Assert.False(OrgUnitPath.IsWithin("/a/bc/", "/a/b/"));
    }

    [Fact]
    public void CreateRoot_and_CreateChild_build_paths_and_depth()
    {
        var orgId = Guid.NewGuid();
        var levelId = Guid.NewGuid();

        var root = OrgUnit.CreateRoot(orgId, levelId, "مدرسة", "School", "S1");
        var child = OrgUnit.CreateChild(root, levelId, "مرحلة", "Stage", "K1");

        Assert.Equal($"/{root.Id:N}/", root.Path);
        Assert.Equal(0, root.Depth);
        Assert.Equal($"/{root.Id:N}/{child.Id:N}/", child.Path);
        Assert.Equal(1, child.Depth);
        Assert.Equal(root.Id, child.ParentId);
        Assert.Equal(orgId, child.OrganizationId);
        Assert.Equal(child.Id, child.OrgUnitId);
    }
}