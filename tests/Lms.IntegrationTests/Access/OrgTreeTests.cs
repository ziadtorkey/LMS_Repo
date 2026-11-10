using Lms.Domain.Organizations;
using Lms.Infrastructure.Organizations;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Access;

[Collection(ApiCollection.Name)]
public sealed class OrgTreeTests(LmsApiFactory factory)
{
    private OrgTree TreeFor(Lms.Infrastructure.Persistence.LmsDbContext db) => new(db);

    [Fact]
    public async Task FindVisible_respects_the_callers_scope()
    {
        var t = await AccessTestData.SeedAsync(factory, "OT1");
        await using var db = factory.CreateDbContext(TestCurrentContext.For(t.Org.Id, t.School1.Path));

        Assert.NotNull(await TreeFor(db).FindVisibleAsync(t.School1.Id, CancellationToken.None));
        Assert.Null(await TreeFor(db).FindVisibleAsync(t.School2.Id, CancellationToken.None));
        Assert.Null(await TreeFor(db).FindVisibleAsync(t.Root.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GetRoot_returns_the_client_root_even_from_a_deep_scope()
    {
        var t = await AccessTestData.SeedAsync(factory, "OT2");
        await using var db = factory.CreateDbContext(TestCurrentContext.For(t.Org.Id, t.School1.Path));

        var root = await TreeFor(db).GetRootAsync(t.Org.Id, CancellationToken.None);

        Assert.Equal(t.Root.Id, root.Id);
    }

    [Fact]
    public async Task GetAncestorsAndSelf_returns_the_chain_from_root_and_never_other_clients_or_deleted_units()
    {
        var a = await AccessTestData.SeedAsync(factory, "OT3");
        var b = await AccessTestData.SeedAsync(factory, "OT4");
        await using var db = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.School1.Path));

        var chain = await TreeFor(db).GetAncestorsAndSelfAsync(a.School1.Path, a.Org.Id, CancellationToken.None);
        Assert.Equal([a.Root.Id, a.School1.Id], chain.Select(u => u.Id));

        // asking with another client's id must not leak that client's units
        var wrongClient = await TreeFor(db).GetAncestorsAndSelfAsync(b.School1.Path, a.Org.Id, CancellationToken.None);
        Assert.Empty(wrongClient);

        await using (var admin = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            admin.Remove(await admin.OrgUnits.SingleAsync(x => x.Id == a.Root.Id));
            await admin.SaveChangesAsync();
        }

        var afterDelete = await TreeFor(db).GetAncestorsAndSelfAsync(a.School1.Path, a.Org.Id, CancellationToken.None);
        Assert.Equal([a.School1.Id], afterDelete.Select(u => u.Id));
    }
}