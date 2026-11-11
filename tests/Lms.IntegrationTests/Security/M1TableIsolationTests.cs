using System.Net.Http.Json;
using Lms.Api.Features.Audit;
using Lms.Domain.AcademicYears;
using Lms.Domain.Auditing;
using Lms.Domain.Notifications;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Auditing;
using Lms.Shared.Authorization;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Table-level isolation for the M1 tables (M1 plan Task 35, PRD §4.2): client B never sees client A's rows, a sibling
/// school never sees its sibling's, a parent sees its descendants, soft-deleted rows are hidden from everyone, and a
/// caller with no client or no scope sees nothing. Org units are the shape every unit-scoped table shares.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M1TableIsolationTests(LmsApiFactory factory)
{
    private static readonly DateOnly Start = new(2041, 9, 1);

    /// <summary>One row per place: client A School1, client A School2, client B root.</summary>
    private sealed record Rows(Guid InSchool1, Guid InSchool2, Guid InClientB);

    private LmsDbContext As(TestCurrentContext context) => factory.CreateDbContext(context);

    private static TestCurrentContext In(AccessTree tree, OrgUnit scope) => TestCurrentContext.For(tree.Org.Id, scope.Path);

    /// <summary>Runs the five isolation expectations against one table, reading the row ids through <paramref name="ids"/>.</summary>
    private async Task AssertUnitScopedIsolationAsync(
        AccessTree a, AccessTree b, Rows rows, Func<LmsDbContext, IQueryable<Guid>> ids, Func<Guid, Task> softDelete)
    {
        var all = new[] { rows.InSchool1, rows.InSchool2, rows.InClientB };

        async Task<List<Guid>> VisibleTo(TestCurrentContext who)
        {
            await using var db = As(who);
            var visible = await ids(db).ToListAsync();
            return visible.Where(all.Contains).Order().ToList();
        }

        // client B never sees client A's rows (and its own is visible at its root)
        Assert.Equal([rows.InClientB], await VisibleTo(In(b, b.Root)));
        // a sibling never sees a sibling
        Assert.Equal([rows.InSchool1], await VisibleTo(In(a, a.School1)));
        Assert.Equal([rows.InSchool2], await VisibleTo(In(a, a.School2)));
        // a parent sees its descendants, never another client
        Assert.Equal(new[] { rows.InSchool1, rows.InSchool2 }.Order(), await VisibleTo(In(a, a.Root)));
        // no scope, no client, malformed scope: nothing
        Assert.Empty(await VisibleTo(TestCurrentContext.For(a.Org.Id, null)));
        Assert.Empty(await VisibleTo(new TestCurrentContext { UserId = Guid.NewGuid() }));
        Assert.Empty(await VisibleTo(TestCurrentContext.For(a.Org.Id, a.School1.Path[..^1])));
        // platform admin sees every client
        Assert.Equal(all.Order(), await VisibleTo(TestCurrentContext.PlatformAdmin()));

        // soft-deleted rows disappear for everyone, siblings stay
        await softDelete(rows.InSchool1);
        Assert.Equal([rows.InSchool2], await VisibleTo(In(a, a.Root)));
        Assert.Empty(await VisibleTo(In(a, a.School1)));
        Assert.Equal(new[] { rows.InSchool2, rows.InClientB }.Order(), await VisibleTo(TestCurrentContext.PlatformAdmin()));
    }

    private async Task<(AccessTree A, AccessTree B)> TwoClientsAsync(string tag) =>
        (await AccessTestData.SeedAsync(factory, tag + "A"), await AccessTestData.SeedAsync(factory, tag + "B"));

    private async Task SoftDeleteAsync<T>(Guid id) where T : class
    {
        await using var db = As(TestCurrentContext.PlatformAdmin());
        db.Remove(await db.Set<T>().SingleAsync(e => EF.Property<Guid>(e, "Id") == id));
        await db.SaveChangesAsync();
    }

    // ---- AcademicYear, Term, CalendarEvent (Tasks 20, 22): unit-scoped.

    private async Task<AcademicYear> AddYearAsync(OrgUnit unit)
    {
        await using var db = As(TestCurrentContext.PlatformAdmin());
        var year = new AcademicYear(unit, "عام", "Year " + unit.Code, Start, Start.AddMonths(10));
        db.Add(year);
        await db.SaveChangesAsync();
        return year;
    }

    [Fact]
    public async Task AcademicYear_is_isolated_by_client_subtree_and_soft_delete()
    {
        var (a, b) = await TwoClientsAsync("TI1");
        var rows = new Rows((await AddYearAsync(a.School1)).Id, (await AddYearAsync(a.School2)).Id, (await AddYearAsync(b.Root)).Id);

        await AssertUnitScopedIsolationAsync(a, b, rows, db => db.Set<AcademicYear>().Select(y => y.Id), SoftDeleteAsync<AcademicYear>);
    }

    [Fact]
    public async Task Term_is_isolated_by_client_subtree_and_soft_delete()
    {
        var (a, b) = await TwoClientsAsync("TI2");
        async Task<Guid> TermAt(OrgUnit unit)
        {
            var year = await AddYearAsync(unit);
            await using var db = As(TestCurrentContext.PlatformAdmin());
            var term = new Term(year, "فصل", "Term " + unit.Code, Start, Start.AddMonths(4));
            db.Add(term);
            await db.SaveChangesAsync();
            return term.Id;
        }

        var rows = new Rows(await TermAt(a.School1), await TermAt(a.School2), await TermAt(b.Root));

        await AssertUnitScopedIsolationAsync(a, b, rows, db => db.Set<Term>().Select(t => t.Id), SoftDeleteAsync<Term>);
    }

    [Fact]
    public async Task CalendarEvent_is_isolated_by_client_subtree_and_soft_delete()
    {
        var (a, b) = await TwoClientsAsync("TI3");
        async Task<Guid> EventAt(OrgUnit unit)
        {
            var year = await AddYearAsync(unit);
            await using var db = As(TestCurrentContext.PlatformAdmin());
            var calendarEvent = new CalendarEvent(year, unit, "عطلة", "Holiday " + unit.Code, CalendarEventKind.Holiday, Start.AddDays(20), Start.AddDays(21));
            db.Add(calendarEvent);
            await db.SaveChangesAsync();
            return calendarEvent.Id;
        }

        var rows = new Rows(await EventAt(a.School1), await EventAt(a.School2), await EventAt(b.Root));

        await AssertUnitScopedIsolationAsync(a, b, rows, db => db.Set<CalendarEvent>().Select(e => e.Id), SoftDeleteAsync<CalendarEvent>);
    }

    // ---- Organization (client status): the tenant root; a client sees only itself, status never widens that.

    [Fact]
    public async Task Organization_is_visible_only_to_its_own_client_whatever_its_status()
    {
        var (a, b) = await TwoClientsAsync("TI4");
        await using (var db = As(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Organizations.SingleAsync(o => o.Id == a.Org.Id)).SetStatus(OrganizationStatus.Suspended);
            await db.SaveChangesAsync();
        }

        async Task<List<Guid>> OrgsSeenBy(TestCurrentContext who)
        {
            await using var db = As(who);
            return (await db.Organizations.Select(o => o.Id).ToListAsync()).Where(id => id == a.Org.Id || id == b.Org.Id).Order().ToList();
        }

        Assert.Equal([a.Org.Id], await OrgsSeenBy(In(a, a.Root)));        // suspended, still its own
        Assert.Equal([b.Org.Id], await OrgsSeenBy(In(b, b.Root)));        // the suspended client does not leak to B
        Assert.Empty(await OrgsSeenBy(new TestCurrentContext { UserId = Guid.NewGuid() }));
        Assert.Equal(new[] { a.Org.Id, b.Org.Id }.Order(), await OrgsSeenBy(TestCurrentContext.PlatformAdmin()));

        await using (var db = As(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(await db.Organizations.SingleAsync(o => o.Id == b.Org.Id));
            await db.SaveChangesAsync();
        }

        Assert.Empty(await OrgsSeenBy(In(b, b.Root)));                      // soft-deleted: hidden from its own client
        Assert.Equal([a.Org.Id], await OrgsSeenBy(TestCurrentContext.PlatformAdmin())); // and from the platform admin
    }

    // ---- NotificationPreference (Task 10): a user-level table, never visible to another user (enforced by the endpoint).

    [Fact]
    public async Task NotificationPreference_a_user_never_reads_or_replaces_another_users_rows()
    {
        var (a, b) = await TwoClientsAsync("TI5");
        var userA = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var colleague = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var otherClient = await AccessTestData.AssignAsync(factory, b, b.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var callers = new CallerClient(factory);

        await callers.PutPreferencesAsync(userA, [new(NotificationEvents.AccessRoleAssigned, NotificationChannels.Email, false, false)]);
        await callers.PutPreferencesAsync(colleague, [new(NotificationEvents.AccessRoleAssigned, NotificationChannels.Email, true, false)]);

        var mineA = await callers.GetPreferencesAsync(userA);
        var mineColleague = await callers.GetPreferencesAsync(colleague);
        var mineOtherClient = await callers.GetPreferencesAsync(otherClient);
        Assert.False(Email(mineA).Enabled);
        Assert.True(Email(mineColleague).Enabled);
        Assert.True(Email(mineOtherClient).Enabled); // the default: nothing stored for this user, nothing inherited from others

        // replacing mine did not touch the colleague's
        await callers.PutPreferencesAsync(userA, [new(NotificationEvents.AccessRoleAssigned, NotificationChannels.Email, true, false)]);
        Assert.True(Email(await callers.GetPreferencesAsync(colleague)).Enabled);

        await using var db = As(TestCurrentContext.PlatformAdmin());
        var stored = await db.Set<NotificationPreference>().Where(p => p.UserId == userA.UserId || p.UserId == colleague.UserId).ToListAsync();
        Assert.All(stored, p => Assert.True(p.UserId == userA.UserId || p.UserId == colleague.UserId));
    }

    private static NotificationPreferenceDto Email(IReadOnlyList<NotificationPreferenceDto> list) =>
        list.Single(p => p.EventKey == NotificationEvents.AccessRoleAssigned && p.Channel == NotificationChannels.Email);

    private sealed class CallerClient(LmsApiFactory factory)
    {
        private HttpRequestMessage Request(Lms.Domain.Access.RoleAssignment who, HttpMethod method)
        {
            var request = new HttpRequestMessage(method, Routes.MyNotificationPreferences);
            request.Headers.Add(TestAuthHandler.UserHeader, who.UserId.ToString());
            request.Headers.Add(ContextHeader.Name, who.Id.ToString());
            return request;
        }

        public async Task PutPreferencesAsync(Lms.Domain.Access.RoleAssignment who, List<NotificationPreferenceDto> preferences)
        {
            var request = Request(who, HttpMethod.Put);
            request.Content = JsonContent.Create(new SetNotificationPreferencesRequest(preferences));
            using var response = await factory.CreateClient().SendAsync(request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }

        public async Task<IReadOnlyList<NotificationPreferenceDto>> GetPreferencesAsync(Lms.Domain.Access.RoleAssignment who)
        {
            using var response = await factory.CreateClient().SendAsync(Request(who, HttpMethod.Get));
            Assert.Equal(200, (int)response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<List<NotificationPreferenceDto>>())!;
        }
    }

    // ---- AuditEntry path visibility (D20): within the viewer's subtree; unit-less entries only to root-level viewers.

    [Fact]
    public async Task AuditEntry_visibility_follows_the_viewers_subtree_and_unit_less_entries_only_reach_root_viewers()
    {
        var (a, b) = await TwoClientsAsync("TI6");
        AuditEntry Entry(Guid? org, string? path) =>
            new(org, Guid.NewGuid(), null, AuditAction.Updated, "Probe", Guid.NewGuid(), null, null, DateTime.UtcNow, null, path);
        var inSchool1 = Entry(a.Org.Id, a.School1.Path);
        var inSchool2 = Entry(a.Org.Id, a.School2.Path);
        var atRoot = Entry(a.Org.Id, a.Root.Path);
        var clientLevel = Entry(a.Org.Id, null);       // e.g. a client-level change or a login
        var ofClientB = Entry(b.Org.Id, b.Root.Path);
        var platformLevel = Entry(null, null);          // outside any client
        await using (var db = As(TestCurrentContext.PlatformAdmin()))
        {
            db.AddRange(inSchool1, inSchool2, atRoot, clientLevel, ofClientB, platformLevel);
            await db.SaveChangesAsync();
        }

        var mine = new[] { inSchool1, inSchool2, atRoot, clientLevel, ofClientB, platformLevel }.Select(e => e.Id).ToHashSet();
        async Task<List<Guid>> VisibleTo(TestCurrentContext who, Guid? organizationFilter = null)
        {
            await using var db = As(who);
            var query = new AuditQuery(organizationFilter, null, null, null, null, null, null);
            var ids = await AuditQueryBuilder.Visible(db.AuditEntries, query, who).Select(e => e.Id).ToListAsync();
            return ids.Where(mine.Contains).Order().ToList();
        }

        Assert.Equal([inSchool1.Id], await VisibleTo(In(a, a.School1)));                       // own subtree only
        Assert.Equal([inSchool2.Id], await VisibleTo(In(a, a.School2)));                       // sibling sees only its own
        Assert.Equal(
            new[] { inSchool1.Id, inSchool2.Id, atRoot.Id, clientLevel.Id }.Order(), await VisibleTo(In(a, a.Root))); // root also gets unit-less
        Assert.Equal([ofClientB.Id], await VisibleTo(In(b, b.Root)));                          // never another client's
        Assert.Empty(await VisibleTo(TestCurrentContext.For(a.Org.Id, null)));
        Assert.Empty(await VisibleTo(new TestCurrentContext { UserId = Guid.NewGuid() }));
        Assert.Empty(await VisibleTo(TestCurrentContext.For(a.Org.Id, a.School1.Path[..^1])));  // malformed scope: nothing
        Assert.Equal(mine.Order(), await VisibleTo(TestCurrentContext.PlatformAdmin()));       // platform admin sees all
        Assert.Equal(
            new[] { inSchool1.Id, inSchool2.Id, atRoot.Id, clientLevel.Id }.Order(),
            await VisibleTo(TestCurrentContext.PlatformAdmin(), a.Org.Id));                    // ...optionally narrowed to one client
    }
}