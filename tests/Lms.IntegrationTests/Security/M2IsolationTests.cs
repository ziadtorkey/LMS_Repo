using Lms.Domain.IdentityProviders;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Table-level isolation for the M2 tables (M2 plan Task 34, PRD §4.2). #181 and #183 cover the basics (client B and the
/// sibling school never see a row, the parent does). This class adds what they leave out: soft-deleted rows are hidden from
/// everyone, a caller with no client or a malformed scope sees nothing, a re-homed person is visible at the new home only,
/// and the history of a status change stays tied to the profile's unit. <c>DataProtectionKey</c> (platform-level, no
/// tenant) is not reachable by any API and has no row here. Student and guardian rows follow with the M3 routes (D23).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M2IsolationTests(LmsApiFactory factory)
{
    private static TestCurrentContext In(AccessTree tree, OrgUnit scope) => TestCurrentContext.For(tree.Org.Id, scope.Path);

    private async Task<(AccessTree A, AccessTree B)> TwoClientsAsync(string tag) =>
        (await AccessTestData.SeedAsync(factory, tag + "A"), await AccessTestData.SeedAsync(factory, tag + "B"));

    /// <summary>The ids of <paramref name="candidates"/> that <paramref name="query"/> returns for a caller.</summary>
    private async Task<List<Guid>> VisibleAsync<T>(
        TestCurrentContext who, Guid[] candidates, Func<DbSet<T>, IQueryable<Guid>> ids) where T : class
    {
        await using var db = factory.CreateDbContext(who);
        var visible = await ids(db.Set<T>()).ToListAsync();
        return visible.Where(candidates.Contains).Order().ToList();
    }

    private async Task SoftDeleteAsync<T>(Guid id) where T : class
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Remove(await db.Set<T>().SingleAsync(e => EF.Property<Guid>(e, "Id") == id));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task StaffProfile_soft_deleted_rows_are_hidden_from_everyone_and_no_scope_sees_nothing()
    {
        var (a, b) = await TwoClientsAsync("M2S1");
        var inSchool1 = await PeopleTestKit.AddStaffAsync(factory, a.School1);
        var inSchool2 = await PeopleTestKit.AddStaffAsync(factory, a.School2);
        var atRoot = await PeopleTestKit.AddStaffAsync(factory, a.Root);
        var ofB = await PeopleTestKit.AddStaffAsync(factory, b.Root);
        Guid[] all = [inSchool1.Id, inSchool2.Id, atRoot.Id, ofB.Id];
        Task<List<Guid>> Seen(TestCurrentContext who) => VisibleAsync<StaffProfile>(who, all, s => s.Select(x => x.Id));

        // a child scope never sees its ancestor's profile through the table, a parent sees its own and its descendants'
        Assert.Equal([inSchool1.Id], await Seen(In(a, a.School1)));
        Assert.Equal(new[] { inSchool1.Id, inSchool2.Id, atRoot.Id }.Order(), await Seen(In(a, a.Root)));
        Assert.Equal([ofB.Id], await Seen(In(b, b.Root)));
        // no scope, no client, malformed scope: nothing
        Assert.Empty(await Seen(TestCurrentContext.For(a.Org.Id, null)));
        Assert.Empty(await Seen(new TestCurrentContext { UserId = Guid.NewGuid() }));
        Assert.Empty(await Seen(TestCurrentContext.For(a.Org.Id, a.School1.Path[..^1])));

        // soft delete hides the row from its own scope, its parent and the platform admin; the siblings stay
        await SoftDeleteAsync<StaffProfile>(inSchool1.Id);
        Assert.Empty(await Seen(In(a, a.School1)));
        Assert.Equal(new[] { inSchool2.Id, atRoot.Id }.Order(), await Seen(In(a, a.Root)));
        Assert.Equal(new[] { inSchool2.Id, atRoot.Id, ofB.Id }.Order(), await Seen(TestCurrentContext.PlatformAdmin()));
    }

    [Fact]
    public async Task StaffProfile_moved_home_is_visible_at_the_new_home_only()
    {
        var (a, _) = await TwoClientsAsync("M2S2");
        var staff = await PeopleTestKit.AddStaffAsync(factory, a.School1);
        Guid[] all = [staff.Id];
        Task<List<Guid>> Seen(TestCurrentContext who) => VisibleAsync<StaffProfile>(who, all, s => s.Select(x => x.Id));
        Assert.Equal([staff.Id], await Seen(In(a, a.School1)));

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var row = await db.Set<StaffProfile>().SingleAsync(s => s.Id == staff.Id);
            row.MoveHome(await db.OrgUnits.SingleAsync(u => u.Id == a.School2.Id));
            await db.SaveChangesAsync();
        }

        Assert.Empty(await Seen(In(a, a.School1)));
        Assert.Equal([staff.Id], await Seen(In(a, a.School2)));
        Assert.Equal([staff.Id], await Seen(In(a, a.Root)));
    }

    [Fact]
    public async Task PersonStatusChange_rows_follow_the_unit_and_are_never_visible_across_clients()
    {
        var (a, b) = await TwoClientsAsync("M2S3");
        var inSchool1 = await PeopleTestKit.AddStaffAsync(factory, a.School1);
        var inSchool2 = await PeopleTestKit.AddStaffAsync(factory, a.School2);
        var ofB = await PeopleTestKit.AddStaffAsync(factory, b.Root);
        var changes = new List<PersonStatusChange>
        {
            PersonStatusChange.For(inSchool1, PersonStatus.Active, PersonStatus.Deactivated, "left", Guid.NewGuid(), DateTime.UtcNow),
            PersonStatusChange.For(inSchool2, PersonStatus.Active, PersonStatus.Deactivated, "left", Guid.NewGuid(), DateTime.UtcNow),
            PersonStatusChange.For(ofB, PersonStatus.Active, PersonStatus.Deactivated, "left", Guid.NewGuid(), DateTime.UtcNow),
        };
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Set<PersonStatusChange>().AddRange(changes);
            await db.SaveChangesAsync();
        }

        var all = changes.Select(c => c.Id).ToArray();
        Task<List<Guid>> Seen(TestCurrentContext who) => VisibleAsync<PersonStatusChange>(who, all, s => s.Select(x => x.Id));

        Assert.Equal([changes[0].Id], await Seen(In(a, a.School1)));
        Assert.Equal([changes[1].Id], await Seen(In(a, a.School2)));
        Assert.Equal(new[] { changes[0].Id, changes[1].Id }.Order(), await Seen(In(a, a.Root)));
        Assert.Equal([changes[2].Id], await Seen(In(b, b.Root)));
        Assert.Empty(await Seen(TestCurrentContext.For(a.Org.Id, null)));
        Assert.Empty(await Seen(new TestCurrentContext { UserId = Guid.NewGuid() }));
        Assert.Equal(all.Order(), await Seen(TestCurrentContext.PlatformAdmin()));
    }

    [Fact]
    public async Task IdentityProviderConfig_is_client_wide_hidden_when_deleted_and_never_crosses_clients()
    {
        var (a, b) = await TwoClientsAsync("M2S4");
        var slugA = "iso-a-" + Guid.NewGuid().ToString("N")[..8];
        var slugB = "iso-b-" + Guid.NewGuid().ToString("N")[..8];
        var ofA = NewProvider(a.Org.Id, slugA);
        var ofB = NewProvider(b.Org.Id, slugB);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Set<IdentityProviderConfig>().AddRange(ofA, ofB);
            await db.SaveChangesAsync();
        }

        Guid[] all = [ofA.Id, ofB.Id];
        Task<List<Guid>> Seen(TestCurrentContext who) => VisibleAsync<IdentityProviderConfig>(who, all, s => s.Select(x => x.Id));

        // client-level table: any unit scope of the client sees it, another client never does
        Assert.Equal([ofA.Id], await Seen(In(a, a.Root)));
        Assert.Equal([ofA.Id], await Seen(In(a, a.School1)));
        Assert.Equal([ofB.Id], await Seen(In(b, b.Root)));
        Assert.Empty(await Seen(new TestCurrentContext { UserId = Guid.NewGuid() }));
        Assert.Equal(all.Order(), await Seen(TestCurrentContext.PlatformAdmin()));

        await SoftDeleteAsync<IdentityProviderConfig>(ofA.Id);
        Assert.Empty(await Seen(In(a, a.Root)));
        Assert.Equal([ofB.Id], await Seen(TestCurrentContext.PlatformAdmin()));
    }

    private static IdentityProviderConfig NewProvider(Guid organizationId, string slug) => new(
        organizationId, IdentityProviderKind.Oidc, slug, "مزود", "Provider", "https://idp.example.test",
        azureTenantId: null, "client-id", "protected-secret", "openid profile email", ExternalMatchBy.Email,
        nationalIdClaim: null, emailDomains: "school.example", enabled: true);
}
