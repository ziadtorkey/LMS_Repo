using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Classes;
using Lms.Domain.Packages;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Packages;

/// <summary>M4b Task 9b (#478, D57): which completed attempt the gradebook holds, and the route that changes the method.</summary>
[Collection(ApiCollection.Name)]
public sealed class GradingMethodTests(LmsApiFactory factory) : IDisposable
{
    private readonly AttemptKit _k = new(factory);

    public void Dispose() => _k.Dispose();

    private async Task<decimal[]> PointsAsync(Guid packageId) => (await _k.GradeEventsAsync(packageId)).Select(e => e.Points).ToArray();

    private Task<HttpResponseMessage> SetMethodAsync(Lms.Domain.Access.RoleAssignment who, Guid packageId, string method) =>
        _k.Host.SendAsync(who, HttpMethod.Put, Routes.PackageGradingMethod(packageId), new UpdatePackageGradingMethodRequest(method));

    [Fact]
    public async Task Highest_writes_a_grade_event_only_when_the_score_rises()
    {
        var w = await _k.SetUpAsync("PG1");
        var (package, _) = await _k.AttachedAsync(w, PackageGradingMethods.Highest);

        await _k.CompleteAsync(w.Student, package.Id, 80);
        await _k.CompleteAsync(w.Student, package.Id, 60); // lower: the graded score stays 80, nothing is written
        await _k.CompleteAsync(w.Student, package.Id, 80); // equal: nothing is written
        await _k.CompleteAsync(w.Student, package.Id, 90);

        Assert.Equal([80m, 90m], await PointsAsync(package.Id));
    }

    [Fact]
    public async Task Last_writes_every_completed_attempt()
    {
        var w = await _k.SetUpAsync("PG2");
        var (package, _) = await _k.AttachedAsync(w, PackageGradingMethods.Last);

        await _k.CompleteAsync(w.Student, package.Id, 80);
        await _k.CompleteAsync(w.Student, package.Id, 60);
        await _k.CompleteAsync(w.Student, package.Id, 70);

        Assert.Equal([80m, 60m, 70m], await PointsAsync(package.Id));
    }

    [Fact]
    public async Task First_grades_only_the_first_completed_attempt_and_later_ones_are_practice()
    {
        var w = await _k.SetUpAsync("PG3");
        var (package, _) = await _k.AttachedAsync(w, PackageGradingMethods.First);

        await _k.CompleteAsync(w.Student, package.Id, 40);
        var practice = await _k.CompleteAsync(w.Student, package.Id, 100);

        Assert.True(practice.Completed); // the attempt itself is complete and recorded
        Assert.Equal([40m], await PointsAsync(package.Id));
    }

    [Fact]
    public async Task Students_are_graded_separately()
    {
        var w = await _k.SetUpAsync("PG4");
        var (other, _) = await _k.Host.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);
        var (package, _) = await _k.AttachedAsync(w, PackageGradingMethods.Highest);

        await _k.CompleteAsync(w.Student, package.Id, 70);
        await _k.CompleteAsync(other, package.Id, 50);

        var events = await _k.GradeEventsAsync(package.Id);
        Assert.Equal(2, events.Select(e => e.StudentProfileId).Distinct().Count());
        Assert.Equal(1, events.Count(e => e.StudentProfileId == w.StudentProfileId));
    }

    [Fact]
    public async Task Changing_the_method_regrades_from_the_completed_attempts_and_writes_only_a_changed_score()
    {
        var w = await _k.SetUpAsync("PG5");
        var (package, _) = await _k.AttachedAsync(w, PackageGradingMethods.Highest);
        await _k.CompleteAsync(w.Student, package.Id, 80);
        await _k.CompleteAsync(w.Student, package.Id, 60);
        Assert.Equal([80m], await PointsAsync(package.Id));

        var toLast = await SetMethodAsync(w.Teacher, package.Id, PackageGradingMethods.Last);
        Assert.Equal(HttpStatusCode.OK, toLast.StatusCode);
        Assert.Equal(PackageGradingMethods.Last, (await toLast.Content.ReadFromJsonAsync<PackageUploadDto>())!.GradingMethod);
        Assert.Equal([80m, 60m], await PointsAsync(package.Id));

        await SetMethodAsync(w.Teacher, package.Id, PackageGradingMethods.Last); // no change: no new event
        Assert.Equal([80m, 60m], await PointsAsync(package.Id));
        await SetMethodAsync(w.Teacher, package.Id, PackageGradingMethods.First); // first completed attempt is 80
        Assert.Equal([80m, 60m, 80m], await PointsAsync(package.Id));

        var state = await (await _k.Host.SendAsync(w.Student, HttpMethod.Get, Routes.PackageMyState(package.Id))).Content.ReadFromJsonAsync<PackageStudentStateDto>();
        Assert.Equal(PackageGradingMethods.First, state!.GradingMethod);
        Assert.Equal(80m, state.GradedScore);
    }
}
