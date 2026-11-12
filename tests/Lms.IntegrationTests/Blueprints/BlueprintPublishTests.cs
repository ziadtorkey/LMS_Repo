using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Auditing;
using Lms.Domain.Blueprints;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Blueprints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Blueprints.BlueprintTestHost;

namespace Lms.IntegrationTests.Blueprints;

/// <summary>Records publishes; reads the row through its own scope to prove the commit happened first.</summary>
public sealed class RecordingPublishedHandler(LmsDbContext db) : IBlueprintPublished
{
    public static readonly ConcurrentQueue<string> Calls = new();

    public async Task<int> OnPublishedAsync(Blueprint blueprint, CancellationToken cancellationToken)
    {
        var status = await db.Set<Blueprint>().Where(b => b.Id == blueprint.Id).Select(b => b.Status).SingleAsync(cancellationToken);
        Calls.Enqueue($"{blueprint.Id}:{blueprint.Version}:{blueprint.ApplyMode}:{status}");
        return 3;
    }
}

[Collection(ApiCollection.Name)]
public sealed class BlueprintPublishTests(LmsApiFactory factory) : IDisposable
{
    private readonly BlueprintTestHost _h = new(factory, s => s.AddScoped<IBlueprintPublished, RecordingPublishedHandler>());

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Supervisor_creates_draft_saves_tree_and_publishes_version_1_then_2()
    {
        var w = await _h.SeedAsync("BP1");
        var supervisor = await _h.SupervisorAsync(w);
        var draft = await _h.CreateDraftAsync(supervisor, w.Course.Id);
        await _h.SaveAsync(supervisor, w.Course.Id, Tree(draft.Summary.Revision, Section("1", Lesson("a"))));

        var first = await _h.PublishAsync(supervisor, w.Course.Id, BlueprintApplyModes.PushToExisting);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var result = (await first.Content.ReadFromJsonAsync<PublishBlueprintResultDto>())!;
        Assert.Equal(1, result.Blueprint.Version);
        Assert.Equal("Published", result.Blueprint.Status);
        Assert.Equal(BlueprintApplyModes.PushToExisting, result.Blueprint.ApplyMode);
        Assert.NotNull(result.Blueprint.PublishedAt);
        Assert.Equal(supervisor.UserId, await PublisherAsync(result.Blueprint.Id));
        Assert.Equal(3, result.ClassesQueued);
        Assert.Contains($"{result.Blueprint.Id}:1:{BlueprintApplyModes.PushToExisting}:Published", RecordingPublishedHandler.Calls);

        var second = await _h.CreateDraftAsync(supervisor, w.Course.Id);
        var edited = await _h.SaveAsync(supervisor, w.Course.Id, Tree(second.Summary.Revision, Section("1", Lesson("a")), Section("2", Lesson("b"))));
        var two = (await (await _h.PublishAsync(supervisor, w.Course.Id)).Content.ReadFromJsonAsync<PublishBlueprintResultDto>())!;

        Assert.Equal(2, two.Blueprint.Version);
        Assert.Equal(edited.Summary.Id, two.Blueprint.Id);
        var versions = (await (await _h.SendAsync(supervisor, HttpMethod.Get, Routes.CourseBlueprints(w.Course.Id))).Content.ReadFromJsonAsync<List<BlueprintSummaryDto>>())!;
        Assert.Equal([2, 1], versions.Select(v => v.Version));
        Assert.Equal(["Published", "Superseded"], versions.Select(v => v.Status));
        // Version 1 stays readable with its own tree.
        var v1 = (await (await _h.SendAsync(supervisor, HttpMethod.Get, Routes.CourseBlueprintVersion(w.Course.Id, 1))).Content.ReadFromJsonAsync<BlueprintDto>())!;
        Assert.Equal(1, v1.Summary.SectionCount);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(supervisor, HttpMethod.Get, Routes.CourseBlueprintVersion(w.Course.Id, 7))).StatusCode);
    }

    [Fact]
    public async Task Publish_empty_blueprint_or_bad_mode_or_missing_draft_is_refused()
    {
        var w = await _h.SeedAsync("BP2");
        var noDraft = await _h.PublishAsync(w.Admin, w.Course.Id);
        var draft = await _h.CreateDraftAsync(w.Admin, w.Course.Id);

        var empty = await _h.PublishAsync(w.Admin, w.Course.Id);
        await _h.SaveAsync(w.Admin, w.Course.Id, Tree(draft.Summary.Revision, Section("lonely")));
        var sectionWithoutLessons = await _h.PublishAsync(w.Admin, w.Course.Id);
        await _h.SaveAsync(w.Admin, w.Course.Id, Tree(draft.Summary.Revision + 1, Section("ok", Lesson("x"))));
        var badMode = await _h.PublishAsync(w.Admin, w.Course.Id, "Whenever");

        Assert.Equal(ProblemCodes.blueprint_no_draft, await CodeAsync(noDraft));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(ProblemCodes.blueprint_empty, await CodeAsync(empty));
        Assert.Equal(ProblemCodes.blueprint_empty, await CodeAsync(sectionWithoutLessons));
        Assert.Equal(HttpStatusCode.BadRequest, badMode.StatusCode);
        // The draft survived every refusal.
        Assert.Equal(HttpStatusCode.OK, (await _h.SendAsync(w.Admin, HttpMethod.Get, Routes.CourseBlueprintDraft(w.Course.Id))).StatusCode);
    }

    [Fact]
    public async Task Publish_is_audited_and_parallel_publishes_have_one_winner()
    {
        var w = await _h.SeedAsync("BP3");
        var draft = await _h.CreateDraftAsync(w.Admin, w.Course.Id);
        await _h.SaveAsync(w.Admin, w.Course.Id, Tree(draft.Summary.Revision, Section("1", Lesson("a"))));

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _h.PublishAsync(w.Admin, w.Course.Id)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entries = await db.AuditEntries.Where(e => e.EntityId == draft.Summary.Id).ToListAsync();
        Assert.Contains(entries, e => e.Action == AuditAction.Updated && e.AfterJson!.Contains("Published"));
        Assert.Equal(1, await db.Set<Blueprint>().CountAsync(b => b.CourseId == w.Course.Id && b.Version == 1));
    }

    [Fact]
    public async Task Drafts_are_hidden_from_view_only_callers_and_inherited_blueprints_are_readable_below()
    {
        var w = await _h.SeedAsync("BP4", ownedAtRoot: true); // owned at the root, read from School 1
        await _h.PublishedAsync(w.Admin, w.Course.Id);
        await _h.CreateDraftAsync(w.Admin, w.Course.Id);
        var school1Admin = await Lms.IntegrationTests.Access.AccessTestData.AssignAsync(
            factory, w.Tree, w.Tree.School1, BuiltInRoles.OrgAdmin, (await Lms.IntegrationTests.People.PeopleTestKit.AddUserAsync(factory)).Id);

        var list = await _h.SendAsync(school1Admin, HttpMethod.Get, Routes.CourseBlueprints(w.Course.Id));
        var version = await _h.SendAsync(school1Admin, HttpMethod.Get, Routes.CourseBlueprintVersion(w.Course.Id, 1));
        var rootList = (await (await _h.SendAsync(w.Admin, HttpMethod.Get, Routes.CourseBlueprints(w.Course.Id))).Content.ReadFromJsonAsync<List<BlueprintSummaryDto>>())!;

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, version.StatusCode);
        Assert.Contains(rootList, v => v.Version == 0); // the owner sees the draft
        // A School 1 admin reads the published versions of the inherited course but cannot edit it (owner unit above them).
        Assert.DoesNotContain((await list.Content.ReadFromJsonAsync<List<BlueprintSummaryDto>>())!, v => v.Version == 0);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(school1Admin, HttpMethod.Post, Routes.CourseBlueprintDraft(w.Course.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(school1Admin, HttpMethod.Get, Routes.CourseBlueprintDraft(w.Course.Id))).StatusCode);
    }

    private async Task<Guid?> PublisherAsync(Guid blueprintId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return (await db.Set<Blueprint>().SingleAsync(b => b.Id == blueprintId)).PublishedBy;
    }
}
