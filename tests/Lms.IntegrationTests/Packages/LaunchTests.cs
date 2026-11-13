using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Content;
using Lms.Domain.Packages;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.Packages;

/// <summary>M4b Task 9b (#478): launching and previewing a package, and the student state that picks the module button (D57).</summary>
[Collection(ApiCollection.Name)]
public sealed class LaunchTests(LmsApiFactory factory) : IDisposable
{
    private readonly AttemptKit _k = new(factory);

    public void Dispose() => _k.Dispose();

    [Fact]
    public async Task Launch_on_in_progress_attempt_returns_same_attempt_and_a_completed_one_starts_a_new_attempt()
    {
        var w = await _k.SetUpAsync("PL1");
        var (package, _) = await _k.AttachedAsync(w);

        var first = await _k.LaunchAsync(w.Student, package.Id);
        var again = await _k.LaunchAsync(w.Student, package.Id);
        await _k.CompleteAsync(w.Student, package.Id, 70); // completes the open attempt
        var afterCompletion = await _k.LaunchAsync(w.Student, package.Id);

        Assert.False(first.Resumed);
        Assert.True(again.Resumed);
        Assert.Equal(first.AttemptId, again.AttemptId);
        Assert.NotEqual(first.LaunchUrl, again.LaunchUrl); // a fresh token each time
        Assert.False(afterCompletion.Resumed);
        Assert.NotEqual(first.AttemptId, afterCompletion.AttemptId);
        Assert.StartsWith(AttemptKit.ContentOrigin + "/p/", first.LaunchUrl);
        Assert.EndsWith("/story.html", first.LaunchUrl);
        Assert.InRange((first.TokenExpiresAtUtc - first.ServerNowUtc).TotalMinutes, 119, 121); // Packages.TokenMinutes = 120
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(2, await db.Set<PackageAttempt>().CountAsync(a => a.PackageId == package.Id));
    }

    [Fact]
    public async Task Concurrent_launches_open_one_attempt()
    {
        var w = await _k.SetUpAsync("PL2");
        var (package, _) = await _k.AttachedAsync(w);

        var launches = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _k.LaunchAsync(w.Student, package.Id)));

        Assert.Single(launches.Select(l => l.AttemptId).Distinct());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await db.Set<PackageAttempt>().CountAsync(a => a.PackageId == package.Id));
    }

    [Fact]
    public async Task Student_cannot_launch_an_invisible_module_or_another_class_course()
    {
        var w = await _k.SetUpAsync("PL3");
        var (hidden, _) = await _k.AttachedAsync(w, visibility: ModuleVisibilities.Hidden);
        var (visible, _) = await _k.AttachedAsync(w);

        Assert.Equal(HttpStatusCode.NotFound, (await _k.Host.SendAsync(w.Student, HttpMethod.Post, Routes.PackageLaunch(hidden.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _k.Host.SendAsync(w.Stranger, HttpMethod.Post, Routes.PackageLaunch(visible.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _k.Host.SendAsync(w.Student, HttpMethod.Post, Routes.PackageLaunch(Guid.NewGuid()))).StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Set<PackageAttempt>().AnyAsync(a => a.PackageId == hidden.Id));
    }

    [Fact]
    public async Task Staff_launch_is_404_and_preview_is_allowed_for_staff_only_and_records_nothing()
    {
        var w = await _k.SetUpAsync("PL4");
        var (package, module) = await _k.AttachedAsync(w, rule: CompletionRules.Score);

        Assert.Equal(HttpStatusCode.NotFound, (await _k.Host.SendAsync(w.Teacher, HttpMethod.Post, Routes.PackageLaunch(package.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _k.Host.SendAsync(w.Admin, HttpMethod.Post, Routes.PackageLaunch(package.Id))).StatusCode);
        var preview = await _k.Host.SendAsync(w.Teacher, HttpMethod.Post, Routes.PackagePreview(package.Id));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var dto = (await preview.Content.ReadFromJsonAsync<PackagePreviewDto>())!;
        Assert.StartsWith(AttemptKit.ContentOrigin + "/p/", dto.LaunchUrl);
        Assert.Equal(HttpStatusCode.Forbidden, (await _k.Host.SendAsync(w.Student, HttpMethod.Post, Routes.PackagePreview(package.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _k.Host.SendAsync(w.OtherTeacher, HttpMethod.Post, Routes.PackagePreview(package.Id))).StatusCode);

        // Preview records no attempt, completion or grade.
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Set<PackageAttempt>().AnyAsync(a => a.PackageId == package.Id));
        Assert.False(await db.Set<ModuleCompletion>().AnyAsync(c => c.ModuleId == module.Id));
        Assert.Empty(await _k.GradeEventsAsync(package.Id));
    }

    [Fact]
    public async Task Launch_and_preview_refuse_without_a_separate_content_origin_and_with_the_flag_off()
    {
        using var noHost = new AttemptKit(factory, origin: null);
        var w = await noHost.SetUpAsync("PL5");
        var (package, _) = await noHost.AttachedAsync(w);
        var launch = await noHost.Host.SendAsync(w.Student, HttpMethod.Post, Routes.PackageLaunch(package.Id));
        var preview = await noHost.Host.SendAsync(w.Teacher, HttpMethod.Post, Routes.PackagePreview(package.Id));

        Assert.Equal(HttpStatusCode.Conflict, launch.StatusCode);
        Assert.Equal(ProblemCodes.package_host_not_configured, await CodeAsync(launch));
        Assert.Equal(ProblemCodes.package_host_not_configured, await CodeAsync(preview));

        var off = await _k.SetUpAsync("PL6", flag: false);
        var (p2, _) = await _k.AttachedAsync(off);
        var flagOff = await _k.Host.SendAsync(off.Student, HttpMethod.Post, Routes.PackageLaunch(p2.Id));
        Assert.Equal(HttpStatusCode.Forbidden, flagOff.StatusCode);
        Assert.Equal(ProblemCodes.feature_disabled, await CodeAsync(flagOff));
    }

    [Fact]
    public async Task My_state_reports_the_method_the_latest_attempt_and_the_graded_score_to_the_enrolled_student_only()
    {
        var w = await _k.SetUpAsync("PL7");
        var (package, _) = await _k.AttachedAsync(w, PackageGradingMethods.Last);
        var empty = await (await _k.Host.SendAsync(w.Student, HttpMethod.Get, Routes.PackageMyState(package.Id))).Content.ReadFromJsonAsync<PackageStudentStateDto>();
        Assert.Equal(PackageGradingMethods.Last, empty!.GradingMethod);
        Assert.Null(empty.LatestAttempt);
        Assert.False(empty.HasCompletedAttempt);
        Assert.Null(empty.GradedScore);

        await _k.CompleteAsync(w.Student, package.Id, 80);
        await _k.CompleteAsync(w.Student, package.Id, 60);
        var open = await _k.LaunchAsync(w.Student, package.Id); // a third, open attempt

        var state = await (await _k.Host.SendAsync(w.Student, HttpMethod.Get, Routes.PackageMyState(package.Id))).Content.ReadFromJsonAsync<PackageStudentStateDto>();
        Assert.Equal(open.AttemptId, state!.LatestAttempt!.Id);
        Assert.Equal(PackageAttemptStatuses.InProgress, state.LatestAttempt.Status);
        Assert.True(state.HasCompletedAttempt);
        Assert.Equal(60m, state.GradedScore); // Last
        Assert.Equal(HttpStatusCode.NotFound, (await _k.Host.SendAsync(w.Teacher, HttpMethod.Get, Routes.PackageMyState(package.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _k.Host.SendAsync(w.Stranger, HttpMethod.Get, Routes.PackageMyState(package.Id))).StatusCode);
    }
}
