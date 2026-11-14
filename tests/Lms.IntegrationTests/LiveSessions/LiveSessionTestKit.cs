using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.Domain.Configuration;
using Lms.Infrastructure.LiveSessions;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.LiveSessions;

/// <summary>Builders for the live session requests, the feature flag and the fake provider.</summary>
internal static class LiveSessionTestKit
{
    public static ModuleSettingsInput Module(string title = "Live", string visibility = ModuleVisibilities.Published, string rule = CompletionRules.None) =>
        new("حصة " + title, title, visibility, null, null, ModuleTargets.ClassCourse, null, rule, null);

    /// <summary>A single session a few days ahead (a Sunday 09:00 UTC so a weekly rule on Sunday matches).</summary>
    public static DateTime NextSunday(int weeksAhead = 1)
    {
        var d = DateTime.UtcNow.Date.AddDays(7 * weeksAhead);
        d = d.AddDays(-(int)d.DayOfWeek);
        return DateTime.SpecifyKind(d.AddHours(9), DateTimeKind.Utc);
    }

    public static CreateLiveSessionRequest Single(string title = "Live", DateTime? start = null, int minutes = 60, string visibility = ModuleVisibilities.Published) =>
        new(Lesson1, Module(title, visibility), start ?? NextSunday(), minutes);

    public static CreateLiveSessionRequest Weekly(int count, string zone = "UTC", DateTime? start = null, string title = "Series") =>
        new(Lesson1, Module(title), start ?? NextSunday(), 60, new LiveRecurrenceInput(1, ["Sunday"], null, count, zone));

    public static async Task EnableAsync(LmsApiFactory factory, ContentWorld w)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new OrgUnitSetting(w.Tree.Org.Id, w.Tree.Root.Id, w.Tree.Root.Path, FeatureFlags.VirtualClasses.Name, "true", false));
        await db.SaveChangesAsync();
    }

    public static async Task SetAsync(LmsApiFactory factory, ContentWorld w, string key, string valueJson)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new OrgUnitSetting(w.Tree.Org.Id, w.Tree.Root.Id, w.Tree.Root.Path, key, valueJson, false));
        await db.SaveChangesAsync();
    }

    public static FakeMeetingProvider Provider(ContentTestHost t) => t.Services.GetRequiredService<FakeMeetingProvider>();

    public static async Task<List<LiveSessionDto>> CreateAsync(ContentTestHost t, RoleAssignment caller, ContentWorld w, CreateLiveSessionRequest request)
    {
        var response = await t.SendAsync(caller, HttpMethod.Post, Routes.ClassCourseLiveSessions(w.ClassCourse.Id), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<LiveSessionDto>>())!;
    }

    public static async Task<List<LiveSessionListItemDto>> ListAsync(ContentTestHost t, RoleAssignment caller, ContentWorld w)
    {
        var response = await t.SendAsync(caller, HttpMethod.Get, Routes.ClassCourseLiveSessions(w.ClassCourse.Id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<LiveSessionListItemDto>>())!.Items.ToList();
    }
}
