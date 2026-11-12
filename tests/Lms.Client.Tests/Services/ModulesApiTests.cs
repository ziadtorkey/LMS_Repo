using System.Net;
using System.Net.Http.Json;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Content;

namespace Lms.Client.Tests.Services;

public sealed class ModulesApiTests
{
    private sealed class Recorder : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private static readonly Guid Cc = Guid.NewGuid(), Lesson = Guid.NewGuid(), Id = Guid.NewGuid();

    [Fact]
    public async Task Http_client_uses_the_contract_routes()
    {
        var recorder = new Recorder();
        var api = new ModulesApi(new ApiHttp(new HttpClient(recorder) { BaseAddress = new Uri("http://localhost/") }, new InMemoryContextStore()));

        await api.DeleteAsync(Id);
        await api.ReorderAsync(Cc, Lesson, [Id]);
        await api.GetAsync(Id).ContinueWith(_ => { }); // empty body: only the request matters
        await api.GetProgressAsync(Cc, Id).ContinueWith(_ => { });

        Assert.Contains((HttpMethod.Delete, Routes.Module(Id)), recorder.Requests);
        Assert.Contains((HttpMethod.Put, Routes.ClassCourseLessonModulesOrder(Cc, Lesson)), recorder.Requests);
        Assert.Contains((HttpMethod.Get, Routes.Module(Id)), recorder.Requests);
        Assert.Contains((HttpMethod.Get, Routes.ClassCourseProgressOf(Cc, Id)), recorder.Requests);
    }

    private static CreateModuleRequest Page(string title = "Intro", string visibility = ModuleVisibilities.Published,
        DateTime? from = null, DateTime? to = null, string target = ModuleTargets.ClassCourse, Guid? group = null) =>
        new(ModuleKinds.Page, title, title, visibility, from, to, target, group, CompletionRules.None, null,
            new ModuleContentInput(BodyHtmlEn: "<p>x</p>"));

    [Fact]
    public async Task Fake_appends_modules_in_order_and_reorders_them()
    {
        var fake = new FakeModulesApi();
        var a = await fake.CreateAsync(Cc, Lesson, Page("A"));
        var b = await fake.CreateAsync(Cc, Lesson, Page("B"));

        await fake.ReorderAsync(Cc, Lesson, [b.Node.Id, a.Node.Id]);

        Assert.Equal(["B", "A"], fake.NodesFor(Cc, Lesson).Select(n => n.TitleEn));
        await Assert.ThrowsAsync<ApiProblemException>(() => fake.ReorderAsync(Cc, Lesson, [a.Node.Id]));
    }

    [Fact]
    public async Task Fake_enforces_the_visibility_range_and_group_target_rules()
    {
        var fake = new FakeModulesApi();
        var now = DateTime.UtcNow;

        var range = await Assert.ThrowsAsync<ApiProblemException>(() =>
            fake.CreateAsync(Cc, Lesson, Page(visibility: ModuleVisibilities.Scheduled, from: now.AddDays(2), to: now.AddDays(1))));
        Assert.Equal(ProblemCodes.module_visibility_range_invalid, range.Code);

        var target = await Assert.ThrowsAsync<ApiProblemException>(() =>
            fake.CreateAsync(Cc, Lesson, Page(target: ModuleTargets.Group, group: Guid.NewGuid())));
        Assert.Equal(ProblemCodes.module_target_invalid, target.Code);

        var group = Guid.NewGuid();
        fake.GroupsByClassCourse[Cc] = [group];
        await fake.CreateAsync(Cc, Lesson, Page(target: ModuleTargets.Group, group: group));
    }

    [Fact]
    public async Task Fake_moves_a_module_to_the_end_of_another_lesson_and_deletes()
    {
        var fake = new FakeModulesApi();
        var other = Guid.NewGuid();
        var a = await fake.CreateAsync(Cc, Lesson, Page("A"));
        var b = await fake.CreateAsync(Cc, Lesson, Page("B"));
        await fake.CreateAsync(Cc, other, Page("C"));

        await fake.MoveAsync(a.Node.Id, other);

        Assert.Equal(["B"], fake.NodesFor(Cc, Lesson).Select(n => n.TitleEn));
        Assert.Equal(["C", "A"], fake.NodesFor(Cc, other).Select(n => n.TitleEn));
        await fake.DeleteAsync(b.Node.Id);
        Assert.Empty(fake.NodesFor(Cc, Lesson));
    }

    [Fact]
    public async Task Fake_completion_is_manual_only()
    {
        var fake = new FakeModulesApi();
        var manual = await fake.CreateAsync(Cc, Lesson, Page() with { CompletionRule = CompletionRules.Manual });
        var none = await fake.CreateAsync(Cc, Lesson, Page());

        var done = await fake.MarkCompleteAsync(manual.Node.Id);

        Assert.Equal(CompletionStates.Completed, done.State);
        await Assert.ThrowsAsync<ApiProblemException>(() => fake.MarkCompleteAsync(none.Node.Id));
        var progress = await fake.GetProgressAsync(Cc);
        Assert.Equal(new ProgressDto(1, 1), progress.Course);
    }
}
