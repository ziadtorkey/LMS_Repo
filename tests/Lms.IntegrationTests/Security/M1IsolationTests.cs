using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.AcademicYears;
using Lms.Shared.Notifications;
using Lms.Domain.Notifications;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Table-level and API-level isolation for every table M1 adds (M1 plan Task 35, PRD §4.2): client A never sees client B,
/// a sibling never sees a sibling, a parent sees its descendants; user-level tables are never visible to another user.
/// Tables still in open PRs (OrgTemplate, NotificationPreference, AcademicYear, Term, CalendarEvent, AuditEntry path
/// visibility) are added here as they merge.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M1IsolationTests(LmsApiFactory factory)
{
    // ---- Notification (Task 10): client-level table (IOptionalTenantOwned) + per-user rows enforced by the endpoints.

    [Fact]
    public async Task Notification_table_client_A_never_sees_client_B_and_no_context_sees_nothing()
    {
        var a = await AccessTestData.SeedAsync(factory, "ISN1A");
        var b = await AccessTestData.SeedAsync(factory, "ISN1B");
        var rowA = await AddNotificationAsync(a.Org.Id, Guid.NewGuid());
        var rowB = await AddNotificationAsync(b.Org.Id, Guid.NewGuid());

        await using (var db = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.Root.Path)))
        {
            var visible = await db.Set<Notification>().Where(n => n.Id == rowA || n.Id == rowB).Select(n => n.Id).ToListAsync();
            Assert.Equal([rowA], visible);
        }

        await using (var db = factory.CreateDbContext(new TestCurrentContext { UserId = Guid.NewGuid() }))
        {
            Assert.False(await db.Set<Notification>().AnyAsync(n => n.Id == rowA || n.Id == rowB));
        }
    }

    [Fact]
    public async Task Notification_api_user_never_sees_or_marks_another_users_rows()
    {
        var a = await AccessTestData.SeedAsync(factory, "ISN2");
        var me = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var colleague = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var mine = await AddNotificationAsync(a.Org.Id, me.UserId);
        var theirs = await AddNotificationAsync(a.Org.Id, colleague.UserId);
        var callers = new CallerClient(factory);

        var ids = await callers.NotificationIdsAsync(me);
        Assert.Contains(mine, ids);
        Assert.DoesNotContain(theirs, ids);

        using var markOther = await callers.SendAsync(me, HttpMethod.Post, $"{Routes.MyNotifications}/{theirs}/read");
        Assert.Equal(404, (int)markOther.StatusCode);
        using var readAll = await callers.SendAsync(me, HttpMethod.Post, Routes.MyNotificationsReadAll);
        Assert.Equal(204, (int)readAll.StatusCode);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Null((await db.Set<Notification>().SingleAsync(n => n.Id == theirs)).ReadAt); // read-all touched only my rows
    }

    [Fact]
    public async Task Notification_api_person_with_two_clients_sees_only_the_active_clients_rows()
    {
        var a = await AccessTestData.SeedAsync(factory, "ISN3A");
        var b = await AccessTestData.SeedAsync(factory, "ISN3B");
        var person = Guid.NewGuid();
        var inA = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, person);
        var inB = await AccessTestData.AssignAsync(factory, b, b.School1, BuiltInRoles.Teacher, person);
        var rowA = await AddNotificationAsync(a.Org.Id, person);
        var rowB = await AddNotificationAsync(b.Org.Id, person);
        var callers = new CallerClient(factory);

        var asA = await callers.NotificationIdsAsync(inA);
        var asB = await callers.NotificationIdsAsync(inB);

        Assert.Contains(rowA, asA);
        Assert.DoesNotContain(rowB, asA);
        Assert.Contains(rowB, asB);
        Assert.DoesNotContain(rowA, asB);
    }

    // ---- AcademicYear and Term (Task 20): unit-scoped tables (OrganizationId + OrgUnitPath).

    [Fact]
    public async Task AcademicYear_and_Term_tables_parent_sees_descendants_sibling_and_other_client_see_nothing()
    {
        var a = await AccessTestData.SeedAsync(factory, "ISY1A");
        var b = await AccessTestData.SeedAsync(factory, "ISY1B");
        var start = new DateOnly(2050, 9, 1);
        var (rootYear, rootTerm) = await AddYearWithTermAsync(a.Root, start);
        var (school1Year, school1Term) = await AddYearWithTermAsync(a.School1, start);
        var (school2Year, school2Term) = await AddYearWithTermAsync(a.School2, start);
        var (clientBYear, clientBTerm) = await AddYearWithTermAsync(b.Root, start);
        Guid[] allYears = [rootYear, school1Year, school2Year, clientBYear];
        Guid[] allTerms = [rootTerm, school1Term, school2Term, clientBTerm];

        // A's root scope: its whole subtree, nothing of client B.
        Assert.Equal([rootYear, school1Year, school2Year], (await VisibleAsync(a.Org.Id, a.Root.Path, allYears, allTerms)).Years);
        Assert.Equal([rootTerm, school1Term, school2Term], (await VisibleAsync(a.Org.Id, a.Root.Path, allYears, allTerms)).Terms);

        // School1 scope: own rows only (not the sibling's; the ancestor's year is reached through the resolver, not the table).
        var school1 = await VisibleAsync(a.Org.Id, a.School1.Path, allYears, allTerms);
        Assert.Equal([school1Year], school1.Years);
        Assert.Equal([school1Term], school1.Terms);

        // Client B: only its own row.
        var clientB = await VisibleAsync(b.Org.Id, b.Root.Path, allYears, allTerms);
        Assert.Equal([clientBYear], clientB.Years);
        Assert.Equal([clientBTerm], clientB.Terms);

        // No scope: nothing (Review Focus 1).
        var none = await VisibleAsync(a.Org.Id, null, allYears, allTerms);
        Assert.Empty(none.Years);
        Assert.Empty(none.Terms);
    }

    // ---- CalendarEvent (Task 22): unit-scoped table.

    [Fact]
    public async Task CalendarEvent_table_parent_sees_descendants_sibling_and_other_client_see_nothing()
    {
        var a = await AccessTestData.SeedAsync(factory, "ISC1A");
        var b = await AccessTestData.SeedAsync(factory, "ISC1B");
        var start = new DateOnly(2051, 9, 1);
        var rootEvent = await AddEventAsync(a.Root, start);
        var school1Event = await AddEventAsync(a.School1, start);
        var school2Event = await AddEventAsync(a.School2, start);
        var clientBEvent = await AddEventAsync(b.Root, start);
        Guid[] all = [rootEvent, school1Event, school2Event, clientBEvent];

        Assert.Equal([rootEvent, school1Event, school2Event], await VisibleEventsAsync(a.Org.Id, a.Root.Path, all));
        Assert.Equal([school1Event], await VisibleEventsAsync(a.Org.Id, a.School1.Path, all));
        Assert.Equal([clientBEvent], await VisibleEventsAsync(b.Org.Id, b.Root.Path, all));
        Assert.Empty(await VisibleEventsAsync(a.Org.Id, null, all));
    }

    private async Task<Guid> AddEventAsync(Lms.Domain.Organizations.OrgUnit unit, DateOnly start)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var year = new AcademicYear(unit, "عام", "Calendar isolation year", start, start.AddMonths(10));
        var holiday = new CalendarEvent(year, unit, "عطلة", "Isolation holiday", CalendarEventKind.Holiday, start.AddDays(5), start.AddDays(5));
        db.AddRange(year, holiday);
        await db.SaveChangesAsync();
        return holiday.Id;
    }

    private async Task<List<Guid>> VisibleEventsAsync(Guid organizationId, string? scopePath, Guid[] ids)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.For(organizationId, scopePath));
        var visible = await db.Set<CalendarEvent>().Where(e => ids.Contains(e.Id)).Select(e => e.Id).ToListAsync();
        return ids.Where(visible.Contains).ToList();
    }

    // ---- NotificationPreference (Task 16): user-level rows, isolated by the endpoint (UserId == caller).

    [Fact]
    public async Task NotificationPreference_api_a_users_choice_never_changes_another_users_preferences()
    {
        var a = await AccessTestData.SeedAsync(factory, "ISP1");
        var me = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var colleague = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var callers = new CallerClient(factory);

        var colleagueBefore = await callers.PreferencesAsync(colleague);
        var mine = await callers.PreferencesAsync(me);
        var target = mine.First(p => !p.Locked);
        var flipped = target with { Enabled = !target.Enabled };

        using (var put = await callers.SendJsonAsync(me, HttpMethod.Put, Routes.MyNotificationPreferences,
                   new SetNotificationPreferencesRequest([flipped])))
        {
            Assert.True(put.IsSuccessStatusCode, $"PUT preferences: {(int)put.StatusCode}");
        }

        Assert.Contains(await callers.PreferencesAsync(me), p => p.EventKey == target.EventKey && p.Channel == target.Channel && p.Enabled == flipped.Enabled);
        Assert.Equal(colleagueBefore, await callers.PreferencesAsync(colleague));
    }

    // ---- AuditEntry path visibility (Task 18, D20): enforced by the audit query, so checked through the API.

    [Fact]
    public async Task Audit_api_subtree_viewer_sees_only_its_subtree_and_unitless_entries_only_at_the_root()
    {
        var a = await AccessTestData.SeedAsync(factory, "ISA1A");
        var b = await AccessTestData.SeedAsync(factory, "ISA1B");
        var rootAdmin = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var school1Admin = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var clientBAdmin = await AccessTestData.AssignAsync(factory, b, b.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var entityType = "QaIsolation" + Guid.NewGuid().ToString("N")[..8];
        var atSchool1 = await AddAuditAsync(a.Org.Id, a.School1.Path, entityType);
        var atSchool2 = await AddAuditAsync(a.Org.Id, a.School2.Path, entityType);
        var unitless = await AddAuditAsync(a.Org.Id, null, entityType);
        var otherClient = await AddAuditAsync(b.Org.Id, b.Root.Path, entityType);
        var callers = new CallerClient(factory);

        Assert.Equal(
            new[] { atSchool1, atSchool2, unitless }.Order(), (await callers.AuditIdsAsync(rootAdmin, entityType)).Order());
        Assert.Equal([atSchool1], await callers.AuditIdsAsync(school1Admin, entityType));
        Assert.Equal([otherClient], await callers.AuditIdsAsync(clientBAdmin, entityType));
    }

    private async Task<Guid> AddAuditAsync(Guid organizationId, string? path, string entityType)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entry = new Lms.Domain.Auditing.AuditEntry(
            organizationId, Guid.NewGuid(), null, Lms.Domain.Auditing.AuditAction.Updated, entityType, Guid.NewGuid(),
            null, null, DateTime.UtcNow, null, path);
        db.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<(Guid Year, Guid Term)> AddYearWithTermAsync(Lms.Domain.Organizations.OrgUnit unit, DateOnly start)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var year = new AcademicYear(unit, "عام", "Isolation year", start, start.AddMonths(10));
        var term = new Term(year, "فصل", "Isolation term", start, start.AddMonths(4));
        db.AddRange(year, term);
        await db.SaveChangesAsync();
        return (year.Id, term.Id);
    }

    private async Task<(List<Guid> Years, List<Guid> Terms)> VisibleAsync(
        Guid organizationId, string? scopePath, Guid[] years, Guid[] terms)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.For(organizationId, scopePath));
        var visibleYears = await db.Set<AcademicYear>().Where(y => years.Contains(y.Id)).Select(y => y.Id).ToListAsync();
        var visibleTerms = await db.Set<Term>().Where(t => terms.Contains(t.Id)).Select(t => t.Id).ToListAsync();
        return (years.Where(visibleYears.Contains).ToList(), terms.Where(visibleTerms.Contains).ToList());
    }

    private async Task<Guid> AddNotificationAsync(Guid organizationId, Guid userId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var row = new Notification(organizationId, userId, "Access.RoleAssigned", "{}", null, DateTime.UtcNow);
        db.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    /// <summary>Sends as the user of a role assignment, in that assignment's context.</summary>
    private sealed class CallerClient(LmsApiFactory factory)
    {
        public Task<HttpResponseMessage> SendAsync(Lms.Domain.Access.RoleAssignment who, HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Add(TestAuthHandler.UserHeader, who.UserId.ToString());
            request.Headers.Add(ContextHeader.Name, who.Id.ToString());
            return factory.CreateClient().SendAsync(request);
        }

        public async Task<List<Guid>> NotificationIdsAsync(Lms.Domain.Access.RoleAssignment who)
        {
            using var response = await SendAsync(who, HttpMethod.Get, $"{Routes.MyNotifications}?pageSize=200");
            Assert.Equal(200, (int)response.StatusCode);
            var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            return page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        }

        public Task<HttpResponseMessage> SendJsonAsync(Lms.Domain.Access.RoleAssignment who, HttpMethod method, string url, object body)
        {
            var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, body.GetType()) };
            request.Headers.Add(TestAuthHandler.UserHeader, who.UserId.ToString());
            request.Headers.Add(ContextHeader.Name, who.Id.ToString());
            return factory.CreateClient().SendAsync(request);
        }

        public async Task<List<NotificationPreferenceDto>> PreferencesAsync(Lms.Domain.Access.RoleAssignment who)
        {
            using var response = await SendAsync(who, HttpMethod.Get, Routes.MyNotificationPreferences);
            Assert.Equal(200, (int)response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<List<NotificationPreferenceDto>>())!
                .OrderBy(p => p.EventKey, StringComparer.Ordinal).ThenBy(p => p.Channel, StringComparer.Ordinal).ToList();
        }

        public async Task<List<Guid>> AuditIdsAsync(Lms.Domain.Access.RoleAssignment who, string entityType)
        {
            using var response = await SendAsync(who, HttpMethod.Get, $"{Routes.Audit}?entityType={entityType}&pageSize=200");
            Assert.Equal(200, (int)response.StatusCode);
            var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            return page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        }
    }
}
