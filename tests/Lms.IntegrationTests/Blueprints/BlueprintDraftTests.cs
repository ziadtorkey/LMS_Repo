using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Auditing;
using Lms.Domain.Blueprints;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Blueprints;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Blueprints.BlueprintTestHost;

namespace Lms.IntegrationTests.Blueprints;

[Collection(ApiCollection.Name)]
public sealed class BlueprintDraftTests(LmsApiFactory factory) : IDisposable
{
    private readonly BlueprintTestHost _h = new(factory);

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Supervisor_creates_draft_saves_tree_and_reads_it_back()
    {
        var w = await _h.SeedAsync("BD1");
        var supervisor = await _h.SupervisorAsync(w);

        var draft = await _h.CreateDraftAsync(supervisor, w.Course.Id);
        var saved = await _h.SaveAsync(supervisor, w.Course.Id,
            Tree(draft.Summary.Revision, Section("1", Lesson("a", 0), Lesson("b", 7)), Section("2", Lesson("c"))));
        var read = await _h.SendAsync(supervisor, HttpMethod.Get, Routes.CourseBlueprintDraft(w.Course.Id));

        Assert.Equal(0, draft.Summary.Version);
        Assert.Equal("Draft", draft.Summary.Status);
        Assert.Empty(draft.Sections);
        Assert.Equal(draft.Summary.Revision + 1, saved.Summary.Revision);
        Assert.Equal(2, saved.Summary.SectionCount);
        Assert.Equal(3, saved.Summary.LessonCount);
        Assert.Equal(["Section 1", "Section 2"], saved.Sections.Select(s => s.TitleEn));
        Assert.Equal([0, 1], saved.Sections.Select(s => s.Order));
        Assert.All(saved.Sections.SelectMany(s => s.Lessons), l => Assert.NotEqual(Guid.Empty, l.Key));
        Assert.Equal(7, saved.Sections[0].Lessons[1].PlannedOffsetDays);
        var again = (await read.Content.ReadFromJsonAsync<BlueprintDto>())!;
        Assert.Equal(saved.Sections.Select(s => s.Key), again.Sections.Select(s => s.Key));
    }

    [Fact]
    public async Task OrgAdmin_and_Manager_may_edit_D46_but_a_Teacher_may_not()
    {
        var w = await _h.SeedAsync("BD2");
        var manager = await _h.RoleAsync(w, BuiltInRoles.Manager);
        var teacher = await _h.RoleAsync(w, BuiltInRoles.Teacher);

        Assert.Equal(HttpStatusCode.Created, (await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.CourseBlueprintDraft(w.Course.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _h.SendAsync(manager, HttpMethod.Post, Routes.CourseBlueprintDraft(w.Course.Id))).StatusCode);
        var save = await _h.SendAsync(manager, HttpMethod.Put, Routes.CourseBlueprintDraft(w.Course.Id), Tree(1, Section("m", Lesson("x"))));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _h.SendAsync(teacher, HttpMethod.Post, Routes.CourseBlueprintDraft(w.Course.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _h.SendAsync(teacher, HttpMethod.Put, Routes.CourseBlueprintDraft(w.Course.Id), Tree(1))).StatusCode);
    }

    [Fact]
    public async Task Unsupervised_supervisor_gets_404_on_edit_and_publish()
    {
        var w = await _h.SeedAsync("BD3");
        var other = await Lms.IntegrationTests.Academic.AcademicTestKit.AddCourseAsync(factory, w.Tree.School1);
        var supervisorOfOther = await _h.SupervisorAsync(w, other);

        foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Post, Routes.CourseBlueprintDraft(w.Course.Id), null),
                     (HttpMethod.Get, Routes.CourseBlueprintDraft(w.Course.Id), null),
                     (HttpMethod.Put, Routes.CourseBlueprintDraft(w.Course.Id), Tree(1)),
                     (HttpMethod.Delete, Routes.CourseBlueprintDraft(w.Course.Id), null),
                     (HttpMethod.Post, Routes.CourseBlueprintPublish(w.Course.Id), new PublishBlueprintRequest(BlueprintApplyModes.NewClassesOnly)),
                     (HttpMethod.Post, Routes.CourseBlueprintCopyFrom(w.Course.Id), new CopyBlueprintRequest(other.Id, null)),
                 })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(supervisorOfOther, method, url, body)).StatusCode);
        }
    }

    [Fact]
    public async Task Second_draft_copies_the_keys_of_the_published_version()
    {
        var w = await _h.SeedAsync("BD4");
        var published = await _h.PublishedAsync(w.Admin, w.Course.Id);

        var draft = await _h.CreateDraftAsync(w.Admin, w.Course.Id);

        Assert.Equal(published.Sections.Select(s => s.Key), draft.Sections.Select(s => s.Key));
        Assert.Equal(published.Sections.SelectMany(s => s.Lessons).Select(l => l.Key), draft.Sections.SelectMany(s => s.Lessons).Select(l => l.Key));
        Assert.Equal(0, draft.Summary.Version);
        // Editing the draft keeps the keys of lessons that are sent back with their key.
        var first = draft.Sections[0];
        var edited = await _h.SaveAsync(w.Admin, w.Course.Id, Tree(draft.Summary.Revision, new BlueprintSectionInput(
            first.Key, "جديد", "Renamed", first.Lessons.Select(l => new BlueprintLessonInput(l.Key, l.TitleAr, l.TitleEn + "!", null, null, l.PlannedOffsetDays)).ToList())));
        Assert.Equal(first.Key, edited.Sections[0].Key);
        Assert.Equal(first.Lessons.Select(l => l.Key), edited.Sections[0].Lessons.Select(l => l.Key));
    }

    [Fact]
    public async Task Draft_exists_returns_409_and_delete_removes_it()
    {
        var w = await _h.SeedAsync("BD5");
        await _h.CreateDraftAsync(w.Admin, w.Course.Id);

        var again = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.CourseBlueprintDraft(w.Course.Id));
        var delete = await _h.SendAsync(w.Admin, HttpMethod.Delete, Routes.CourseBlueprintDraft(w.Course.Id));
        var gone = await _h.SendAsync(w.Admin, HttpMethod.Get, Routes.CourseBlueprintDraft(w.Course.Id));

        Assert.Equal(ProblemCodes.blueprint_draft_exists, await CodeAsync(again));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(ProblemCodes.blueprint_no_draft, await CodeAsync(gone));
        Assert.Equal(HttpStatusCode.Created, (await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.CourseBlueprintDraft(w.Course.Id))).StatusCode);
    }

    [Fact]
    public async Task Stale_revision_is_409_and_parallel_saves_have_one_winner()
    {
        var w = await _h.SeedAsync("BD6");
        var draft = await _h.CreateDraftAsync(w.Admin, w.Course.Id);
        await _h.SaveAsync(w.Admin, w.Course.Id, Tree(draft.Summary.Revision, Section("a", Lesson("x"))));

        var stale = await _h.SendAsync(w.Admin, HttpMethod.Put, Routes.CourseBlueprintDraft(w.Course.Id), Tree(draft.Summary.Revision, Section("b", Lesson("y"))));
        var current = (await (await _h.SendAsync(w.Admin, HttpMethod.Get, Routes.CourseBlueprintDraft(w.Course.Id))).Content.ReadFromJsonAsync<BlueprintDto>())!;
        var parallel = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            _h.SendAsync(w.Admin, HttpMethod.Put, Routes.CourseBlueprintDraft(w.Course.Id), Tree(current.Summary.Revision, Section("p" + i, Lesson("q"))))));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ProblemCodes.blueprint_stale, await CodeAsync(stale));
        Assert.Equal(1, parallel.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, parallel.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        foreach (var loser in parallel.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Equal(ProblemCodes.blueprint_stale, await CodeAsync(loser));
        }

        var final = (await (await _h.SendAsync(w.Admin, HttpMethod.Get, Routes.CourseBlueprintDraft(w.Course.Id))).Content.ReadFromJsonAsync<BlueprintDto>())!;
        Assert.Equal(1, final.Summary.SectionCount); // one whole tree, never a mix of two saves
        Assert.Equal(1, final.Summary.LessonCount);
    }

    [Fact]
    public async Task Invalid_trees_are_400_and_change_nothing()
    {
        var w = await _h.SeedAsync("BD7");
        var other = await Lms.IntegrationTests.Academic.AcademicTestKit.AddCourseAsync(factory, w.Tree.School1);
        var otherPublished = await _h.PublishedAsync(w.Admin, other.Id);
        var draft = await _h.CreateDraftAsync(w.Admin, w.Course.Id);
        var revision = draft.Summary.Revision;
        var sharedKey = Guid.NewGuid();
        var tooManyLessons = Enumerable.Range(0, 501).Select(i => Lesson("l" + i)).ToArray();

        var bad = new[]
        {
            Tree(revision, new BlueprintSectionInput(sharedKey, "ا", "A", []), new BlueprintSectionInput(sharedKey, "ب", "B", [])), // duplicate keys
            Tree(revision, new BlueprintSectionInput(Guid.NewGuid(), "ا", "A", [])),                                              // key not from this course
            Tree(revision, new BlueprintSectionInput(otherPublished.Sections[0].Key, "ا", "A", [])),                               // key of another course
            Tree(revision, Section("x", new BlueprintLessonInput(null, "", "L", null, null, null))),                              // empty title
            Tree(revision, Section("x", new BlueprintLessonInput(null, "ا", new string('x', 201), null, null, null))),             // long title
            Tree(revision, Section("x", new BlueprintLessonInput(null, "ا", "L", new string('x', 4001), null, null))),              // long objectives
            Tree(revision, Section("x", Lesson("neg", -1))),                                                                      // offset below range
            Tree(revision, Section("x", Lesson("far", 401))),                                                                     // offset above range
            Tree(revision, Enumerable.Range(0, 101).Select(i => Section("s" + i)).ToArray()),                                    // 101 sections
            Tree(revision, Section("big", tooManyLessons)),                                                                       // 501 lessons
        };

        foreach (var request in bad)
        {
            var response = await _h.SendAsync(w.Admin, HttpMethod.Put, Routes.CourseBlueprintDraft(w.Course.Id), request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var unchanged = (await (await _h.SendAsync(w.Admin, HttpMethod.Get, Routes.CourseBlueprintDraft(w.Course.Id))).Content.ReadFromJsonAsync<BlueprintDto>())!;
        Assert.Equal(revision, unchanged.Summary.Revision);
        Assert.Empty(unchanged.Sections);
    }

    [Fact]
    public async Task Copy_from_other_course_gets_new_keys_and_can_use_an_older_version()
    {
        var w = await _h.SeedAsync("BD8");
        var source = await Lms.IntegrationTests.Academic.AcademicTestKit.AddCourseAsync(factory, w.Tree.School1);
        var v1 = await _h.PublishedAsync(w.Admin, source.Id);
        var draftOfSource = await _h.CreateDraftAsync(w.Admin, source.Id);
        await _h.SaveAsync(w.Admin, source.Id, Tree(draftOfSource.Summary.Revision, Section("only in v2", Lesson("z"))));
        Assert.Equal(HttpStatusCode.OK, (await _h.PublishAsync(w.Admin, source.Id)).StatusCode);
        var target = await _h.CreateDraftAsync(w.Admin, w.Course.Id);

        var latest = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.CourseBlueprintCopyFrom(w.Course.Id), new CopyBlueprintRequest(source.Id, null));
        var older = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.CourseBlueprintCopyFrom(w.Course.Id), new CopyBlueprintRequest(source.Id, 1));
        var copiedFromV1 = (await older.Content.ReadFromJsonAsync<BlueprintDto>())!;

        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        Assert.Equal("Section only in v2", (await latest.Content.ReadFromJsonAsync<BlueprintDto>())!.Sections[0].TitleEn);
        Assert.Equal(HttpStatusCode.OK, older.StatusCode);
        Assert.Equal(v1.Sections[0].TitleEn, copiedFromV1.Sections[0].TitleEn);
        Assert.NotEqual(v1.Sections[0].Key, copiedFromV1.Sections[0].Key);
        Assert.Empty(copiedFromV1.Sections.SelectMany(s => s.Lessons).Select(l => l.Key).Intersect(v1.Sections.SelectMany(s => s.Lessons).Select(l => l.Key)));
        Assert.True(copiedFromV1.Summary.Revision > target.Summary.Revision);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.CourseBlueprintCopyFrom(w.Course.Id), new CopyBlueprintRequest(source.Id, 9))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.CourseBlueprintCopyFrom(w.Course.Id), new CopyBlueprintRequest(Guid.NewGuid(), null))).StatusCode);
    }

    [Fact]
    public async Task Copy_from_a_course_of_another_client_is_404_and_needs_a_draft()
    {
        var w = await _h.SeedAsync("BD9");
        var other = await _h.SeedAsync("BD9B");
        await _h.PublishedAsync(other.Admin, other.Course.Id);

        var foreign = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.CourseBlueprintCopyFrom(w.Course.Id), new CopyBlueprintRequest(other.Course.Id, null));
        var noDraft = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.CourseBlueprintCopyFrom(w.Course.Id), new CopyBlueprintRequest(w.Course.Id, null));

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noDraft.StatusCode);
        Assert.Equal(ProblemCodes.blueprint_no_draft, await CodeAsync(noDraft));
    }

    [Fact]
    public async Task Only_one_draft_per_course_even_concurrently()
    {
        var w = await _h.SeedAsync("BD10");

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _h.SendAsync(w.Admin, HttpMethod.Post, Routes.CourseBlueprintDraft(w.Course.Id))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Equal(ProblemCodes.blueprint_draft_exists, await CodeAsync(conflict));
        }

        await using var db = factory.CreateDbContext(Lms.IntegrationTests.Infrastructure.TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await db.Set<Blueprint>().CountAsync(b => b.CourseId == w.Course.Id));
    }

    [Fact]
    public async Task Blueprint_rows_are_isolated_by_client_and_unit()
    {
        var w = await _h.SeedAsync("BD11");
        var other = await _h.SeedAsync("BD11B");
        var school2Admin = await Lms.IntegrationTests.Access.AccessTestData.AssignAsync(
            factory, w.Tree, w.Tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await _h.PublishedAsync(w.Admin, w.Course.Id);

        foreach (var outsider in new[] { other.Admin, school2Admin })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(outsider, HttpMethod.Get, Routes.CourseBlueprints(w.Course.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(outsider, HttpMethod.Get, Routes.CourseBlueprintVersion(w.Course.Id, 1))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(outsider, HttpMethod.Post, Routes.CourseBlueprintDraft(w.Course.Id))).StatusCode);
        }

        await using var db = factory.CreateDbContext(Lms.IntegrationTests.Infrastructure.TestCurrentContext.For(other.Tree.Org.Id, other.Tree.Root.Path));
        Assert.Empty(await db.Set<Blueprint>().ToListAsync());
        Assert.Empty(await db.Set<BlueprintSection>().ToListAsync());
        Assert.Empty(await db.Set<BlueprintLesson>().ToListAsync());
    }

    [Fact]
    public async Task Draft_changes_are_audited()
    {
        var w = await _h.SeedAsync("BD12");
        var draft = await _h.CreateDraftAsync(w.Admin, w.Course.Id);
        await _h.SaveAsync(w.Admin, w.Course.Id, Tree(draft.Summary.Revision, Section("a", Lesson("x"))));

        await using var db = factory.CreateDbContext(Lms.IntegrationTests.Infrastructure.TestCurrentContext.PlatformAdmin());
        var entries = await db.AuditEntries.Where(e => e.EntityId == draft.Summary.Id).OrderBy(e => e.TimestampUtc).ThenBy(e => e.Id).ToListAsync();

        Assert.Equal(AuditAction.Created, entries[0].Action);
        Assert.All(entries, e => Assert.Equal(nameof(Blueprint), e.EntityType));
        Assert.Contains(entries, e => e.Action == AuditAction.Updated);
        Assert.All(entries, e => Assert.Equal(w.Tree.School1.Path, e.OrgUnitPath));
    }
}
