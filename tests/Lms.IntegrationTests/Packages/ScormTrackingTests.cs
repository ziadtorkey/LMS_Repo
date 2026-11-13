using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Grading;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Content;
using Lms.Shared.Packages;

namespace Lms.IntegrationTests.Packages;

/// <summary>The real SCORM tracker behind the commit and finish routes (M4b Task 10): what a student's client reports is judged before it can reach the gradebook.</summary>
[Collection(ApiCollection.Name)]
public sealed class ScormTrackingTests(LmsApiFactory factory) : IDisposable
{
    private readonly AttemptKit _k = new(factory, realScormTracker: true);

    public void Dispose() => _k.Dispose();

    [Fact]
    public async Task An_absurd_score_is_dropped_and_writes_no_grade_event_but_a_sane_one_does()
    {
        var w = await _k.SetUpAsync("ST1");
        var (package, _) = await _k.AttachedAsync(w, rule: CompletionRules.Submit);
        var first = await _k.LaunchAsync(w.Student, package.Id);
        await _k.CommitAsync(w.Student, first.AttemptId, 0, ("cmi.core.lesson_status", "passed"), ("cmi.core.score.raw", "1000000"), ("cmi.core.score.max", "100"));

        var done = await (await _k.FinishAsync(w.Student, first.AttemptId, 0)).Content.ReadFromJsonAsync<PackageAttemptDto>();

        Assert.True(done!.Completed);
        Assert.Null(done.ScoreRaw);
        Assert.DoesNotContain(await _k.GradeEventsAsync(package.Id), e => e.Points > e.MaxPoints || e.Points < 0);

        var second = await _k.LaunchAsync(w.Student, package.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await _k.CommitAsync(w.Student, second.AttemptId, 0,
            ("cmi.core.lesson_status", "passed"), ("cmi.core.score.raw", "85"), ("cmi.core.score.max", "100"))).StatusCode);
        var good = await (await _k.FinishAsync(w.Student, second.AttemptId, 0)).Content.ReadFromJsonAsync<PackageAttemptDto>();

        Assert.Equal(85m, good!.ScoreRaw);
        var events = await _k.GradeEventsAsync(package.Id);
        var grade = Assert.Single(events);
        Assert.Equal((85m, 100m, GradeSourceKind.Scorm), (grade.Points, grade.MaxPoints, grade.SourceKind));
    }
}
