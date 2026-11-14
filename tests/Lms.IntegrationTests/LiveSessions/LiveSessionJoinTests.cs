using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Content;
using Lms.Domain.LiveSessions;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Content.ContentTestHost;
using static Lms.IntegrationTests.LiveSessions.LiveSessionTestKit;

namespace Lms.IntegrationTests.LiveSessions;

/// <summary>M4b Task 6 (#473): the join window, the URLs a caller may receive and the completion a join records.</summary>
[Collection(ApiCollection.Name)]
public sealed class LiveSessionJoinTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private static CreateLiveSessionRequest At(DateTime start, string title = "Live", string visibility = ModuleVisibilities.Published, string rule = CompletionRules.None, int minutes = 60) =>
        new(Lesson1, Module(title, visibility, rule), start, minutes);

    private async Task<LiveSessionDto> CreateAtAsync(ContentWorld w, DateTime start, string visibility = ModuleVisibilities.Published, string rule = CompletionRules.None, int minutes = 60) =>
        Assert.Single(await CreateAsync(_t, w.Teacher, w, At(start, "Live", visibility, rule, minutes)));

    private Task<HttpResponseMessage> JoinAsync(Lms.Domain.Access.RoleAssignment who, Guid id) =>
        _t.SendAsync(who, HttpMethod.Post, Routes.LiveSessionJoin(id));

    [Fact]
    public async Task Join_outside_window_is_409_and_invisible_module_is_404()
    {
        var w = await _t.SetUpAsync("LJ1");
        await EnableAsync(factory, w);
        var far = await CreateAtAsync(w, DateTime.UtcNow.AddDays(2));
        var ended = await CreateAtAsync(w, DateTime.UtcNow.AddHours(-2), minutes: 30);
        var hidden = await CreateAtAsync(w, DateTime.UtcNow.AddMinutes(5), ModuleVisibilities.Hidden);
        var open = await CreateAtAsync(w, DateTime.UtcNow.AddMinutes(5));

        var tooEarly = await JoinAsync(w.Student, far.Id);
        var tooLate = await JoinAsync(w.Student, ended.Id);

        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        Assert.Equal(ProblemCodes.live_session_not_open, await CodeAsync(tooEarly));
        Assert.Equal(ProblemCodes.live_session_not_open, await CodeAsync(tooLate));
        Assert.Equal(HttpStatusCode.NotFound, (await JoinAsync(w.Student, hidden.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await JoinAsync(w.Stranger, open.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await JoinAsync(w.OtherTeacher, open.Id)).StatusCode);
        var joined = await JoinAsync(w.Student, open.Id);
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        var dto = (await joined.Content.ReadFromJsonAsync<LiveSessionJoinDto>())!;
        Assert.InRange(Math.Abs((open.EndsAtUtc - dto.ExpiresAtUtc).TotalMilliseconds), 0, 1);
        Assert.InRange((DateTime.UtcNow - dto.ServerNowUtc).TotalSeconds, -5, 5); // the server clock, not the caller's
        Assert.Contains("no-store", joined.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Join_window_follows_the_setting()
    {
        var w = await _t.SetUpAsync("LJ2");
        await EnableAsync(factory, w);
        var in40 = await CreateAtAsync(w, DateTime.UtcNow.AddMinutes(40));
        Assert.Equal(HttpStatusCode.Conflict, (await JoinAsync(w.Student, in40.Id)).StatusCode);

        await SetAsync(factory, w, Lms.Shared.Configuration.SettingKeys.LiveJoinWindowMinutes.Name, "60");

        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(w.Student, in40.Id)).StatusCode);
    }

    [Fact]
    public async Task Student_never_receives_the_start_url_and_the_manager_does()
    {
        var w = await _t.SetUpAsync("LJ3");
        await EnableAsync(factory, w);
        var session = await CreateAtAsync(w, DateTime.UtcNow.AddMinutes(5));
        var far = await CreateAtAsync(w, DateTime.UtcNow.AddDays(3));

        var student = await (await JoinAsync(w.Student, session.Id)).Content.ReadFromJsonAsync<LiveSessionJoinDto>();
        var teacher = await (await JoinAsync(w.Teacher, session.Id)).Content.ReadFromJsonAsync<LiveSessionJoinDto>();
        var teacherEarly = await JoinAsync(w.Teacher, far.Id); // staff are not held to the window

        Assert.Contains("pwd=join", student!.Url);
        Assert.DoesNotContain("zak=host", student.Url);
        Assert.Contains("zak=host", teacher!.Url);
        Assert.Equal(HttpStatusCode.OK, teacherEarly.StatusCode);
    }

    [Fact]
    public async Task Join_records_viewed_completion_for_students_only()
    {
        var w = await _t.SetUpAsync("LJ4");
        await EnableAsync(factory, w);
        var viewRule = await CreateAtAsync(w, DateTime.UtcNow.AddMinutes(5), rule: CompletionRules.View);
        var manualRule = await CreateAtAsync(w, DateTime.UtcNow.AddMinutes(5), rule: CompletionRules.Manual);

        await JoinAsync(w.Teacher, viewRule.Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            Assert.False(await db.Set<ModuleCompletion>().AnyAsync(c => c.ModuleId == viewRule.Module.Id));
        }

        await JoinAsync(w.Student, viewRule.Id);
        await JoinAsync(w.Student, manualRule.Id);
        await JoinAsync(w.Student, viewRule.Id); // idempotent

        await using var verify = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var completions = await verify.Set<ModuleCompletion>().Where(c => c.StudentId == w.StudentProfileId).ToListAsync();
        Assert.Equal(ModuleCompletion.CompletedState, completions.Single(c => c.ModuleId == viewRule.Module.Id).State);
        Assert.Equal(ModuleCompletion.InProgressState, completions.Single(c => c.ModuleId == manualRule.Module.Id).State);
        Assert.Equal(2, completions.Count);
    }

    [Fact]
    public async Task Join_is_refused_when_the_flag_is_off_or_the_provider_never_confirmed_the_meeting()
    {
        var w = await _t.SetUpAsync("LJ5");
        await EnableAsync(factory, w);
        Provider(_t).FailNext(1);
        var unsynced = await CreateAtAsync(w, DateTime.UtcNow.AddMinutes(5));
        var synced = await CreateAtAsync(w, DateTime.UtcNow.AddMinutes(5));

        var noMeeting = await JoinAsync(w.Student, unsynced.Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var setting = await db.Set<Lms.Domain.Configuration.OrgUnitSetting>().SingleAsync(s => s.OrganizationId == w.Tree.Org.Id && s.Key == Lms.Shared.Configuration.FeatureFlags.VirtualClasses.Name);
            db.Remove(setting);
            await db.SaveChangesAsync();
        }

        var flagOff = await JoinAsync(w.Student, synced.Id);

        Assert.Equal(ProblemCodes.live_session_not_open, await CodeAsync(noMeeting));
        Assert.Equal(HttpStatusCode.Forbidden, flagOff.StatusCode);
        Assert.Equal(ProblemCodes.feature_disabled, await CodeAsync(flagOff));
    }

    [Fact]
    public async Task Join_is_refused_for_a_cancelled_or_ended_session()
    {
        var w = await _t.SetUpAsync("LJ6");
        await EnableAsync(factory, w);
        var session = await CreateAtAsync(w, DateTime.UtcNow.AddMinutes(5));
        using (var scope = _t.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<Lms.Api.Infrastructure.HttpCurrentContext>().SetUser(Guid.Empty, true);
            Assert.True(await scope.ServiceProvider.GetRequiredService<Lms.Infrastructure.LiveSessions.LiveSessionLifecycle>().EndedAsync(session.Id, default));
        }

        Assert.Equal(HttpStatusCode.Conflict, (await JoinAsync(w.Student, session.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await JoinAsync(w.Teacher, session.Id)).StatusCode);
        await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.LiveSession(session.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await JoinAsync(w.Student, session.Id)).StatusCode);
    }
}
