using System.Net;
using Lms.Domain.Library;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Library;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Library.LibraryTestWorld;

namespace Lms.IntegrationTests.Library;

[Collection(ApiCollection.Name)]
public sealed class LibraryTests(LmsApiFactory factory)
{
    [Fact]
    public async Task Admin_creates_edits_and_deletes_a_link_resource()
    {
        using var w = await LibraryTestWorld.CreateAsync(factory, "LB1");

        var created = await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, "Khan Academy", tags: ["math", "Math", " video "], levels: [w.LevelOfSchool1]));

        Assert.Equal(["math", "video"], created.Tags);
        Assert.Equal([w.LevelOfSchool1], created.LevelTypeIds);
        Assert.True(created.CanEdit);
        Assert.False(created.IsInherited);
        Assert.Equal(w.Tree.School1.Id, created.OrgUnitId);
        var edited = await w.ReadAsync<LibraryResourceDto>(await w.SendAsync(w.School1Admin, HttpMethod.Put, Routes.LibraryResource(created.Id),
            Link(w.Tree.School1, "Khan Academy 2", "https://example.org/b", tags: ["science"])));
        Assert.Equal("Khan Academy 2", edited.TitleEn);
        Assert.Equal(["science"], edited.Tags);
        Assert.Empty(edited.LevelTypeIds);
        Assert.Equal(HttpStatusCode.NoContent, (await w.SendAsync(w.School1Admin, HttpMethod.Delete, Routes.LibraryResource(created.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.School1Admin, HttpMethod.Get, Routes.LibraryResource(created.Id))).StatusCode);
        Assert.Empty((await w.ListAsync(w.School1Admin)).Items);
    }

    [Fact]
    public async Task Resource_visible_below_owner_not_in_sibling_school()
    {
        using var w = await LibraryTestWorld.CreateAsync(factory, "LB2");
        var atRoot = await w.CreateAsync(w.Admin, Link(w.Tree.Root, "Group wide"));
        var atSchool1 = await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, "School 1 only"));

        var school1 = await w.ListAsync(w.School1Admin);
        var school2 = await w.ListAsync(w.School2Admin);
        var root = await w.ListAsync(w.Admin);
        var rootBrowsingSchool1 = await w.ListAsync(w.Admin, "?orgUnitId=" + w.Tree.School1.Id);

        Assert.Equal(new[] { atRoot.Id, atSchool1.Id }.Order().ToList(), school1.Items.Select(r => r.Id).Order().ToList());
        Assert.Equal([atRoot.Id], school2.Items.Select(r => r.Id));
        Assert.Equal([atRoot.Id], root.Items.Select(r => r.Id));
        Assert.Equal(2, rootBrowsingSchool1.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.School2Admin, HttpMethod.Get, Routes.LibraryResource(atSchool1.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.School2Admin, HttpMethod.Get, Routes.Library + "?orgUnitId=" + w.Tree.School1.Id)).StatusCode);
    }

    [Fact]
    public async Task Inherited_resource_is_read_only_below_its_owner()
    {
        using var w = await LibraryTestWorld.CreateAsync(factory, "LB3");
        var atRoot = await w.CreateAsync(w.Admin, Link(w.Tree.Root, "Group wide"));

        var seen = (await w.ListAsync(w.School1Admin)).Items.Single();
        var seenByTeacher = await w.ReadAsync<LibraryResourceDto>(await w.SendAsync(w.Teacher, HttpMethod.Get, Routes.LibraryResource(atRoot.Id)));

        Assert.True(seen.IsInherited);
        Assert.False(seen.CanEdit);
        Assert.True(seenByTeacher.IsInherited);
        Assert.False(seenByTeacher.CanEdit); // the Teacher's Library.Manage is unit-wide but the owner is above
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.School1Admin, HttpMethod.Put, Routes.LibraryResource(atRoot.Id), Link(w.Tree.Root, "Hijack"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.School1Admin, HttpMethod.Delete, Routes.LibraryResource(atRoot.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.Teacher, HttpMethod.Delete, Routes.LibraryResource(atRoot.Id))).StatusCode);
        Assert.Equal("Group wide", (await w.ListAsync(w.Admin)).Items.Single().TitleEn);
    }

    [Fact]
    public async Task The_owner_of_a_resource_cannot_change()
    {
        using var w = await LibraryTestWorld.CreateAsync(factory, "LB4");
        var created = await w.CreateAsync(w.Admin, Link(w.Tree.School1, "Mine"));

        var response = await w.SendAsync(w.Admin, HttpMethod.Put, Routes.LibraryResource(created.Id), Link(w.Tree.School2, "Moved"));

        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (response.StatusCode, await CodeAsync(response)));
    }

    [Fact]
    public async Task Teacher_manages_the_library_unit_wide_and_student_cannot()
    {
        using var w = await LibraryTestWorld.CreateAsync(factory, "LB5");

        var byTeacher = await w.CreateAsync(w.Teacher, Link(w.Tree.School1, "Teacher made"));
        Assert.True(byTeacher.CanEdit);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.Teacher, HttpMethod.Post, Routes.Library, Link(w.Tree.School2))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(w.Student, HttpMethod.Post, Routes.Library, Link(w.Tree.School1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(w.Student, HttpMethod.Put, Routes.LibraryResource(byTeacher.Id), Link(w.Tree.School1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(w.Student, HttpMethod.Delete, Routes.LibraryResource(byTeacher.Id))).StatusCode);
        var asStudent = await w.ReadAsync<LibraryResourceDto>(await w.SendAsync(w.Student, HttpMethod.Get, Routes.LibraryResource(byTeacher.Id)));
        Assert.False(asStudent.CanEdit);
    }

    [Fact]
    public async Task Student_sees_only_matching_levels()
    {
        using var w = await LibraryTestWorld.CreateAsync(factory, "LB6");
        var everyone = await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, "For all"));
        var mine = await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, "My level", levels: [w.LevelOfSchool1]));
        var other = await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, "Other level", levels: [w.OtherLevel]));

        var student = await w.ListAsync(w.Student);
        var staff = await w.ListAsync(w.School1Admin);

        Assert.Equal(new[] { everyone.Id, mine.Id }.Order(), student.Items.Select(r => r.Id).Order());
        Assert.Equal(3, staff.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.Student, HttpMethod.Get, Routes.LibraryResource(other.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.SendAsync(w.Student, HttpMethod.Get, Routes.LibraryResource(mine.Id))).StatusCode);
    }

    [Fact]
    public async Task Filters_compose()
    {
        using var w = await LibraryTestWorld.CreateAsync(factory, "LB7");
        var course = await AcademicTestKit.AddCourseAsync(factory, w.Tree.School1);
        await AcademicTestKit.AllocateAsync(factory, course, w.Tree.School1);
        var a = await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, "Algebra video", tags: ["math"], courseId: course.Id, levels: [w.LevelOfSchool1]));
        var b = await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, "Algebra notes", tags: ["math", "notes"]));
        var c = await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, "History", tags: ["history"], levels: [w.OtherLevel]));

        async Task<List<Guid>> IdsAsync(string query) => (await w.ListAsync(w.School1Admin, query)).Items.Select(r => r.Id).Order().ToList();

        Assert.Equal(new[] { a.Id, b.Id }.Order().ToList(), await IdsAsync("?search=algebra"));
        Assert.Equal([a.Id], await IdsAsync("?search=algebra&courseId=" + course.Id));
        Assert.Equal(new[] { a.Id, b.Id }.Order().ToList(), await IdsAsync("?tag=math"));
        Assert.Equal([b.Id], await IdsAsync("?tag=notes&search=algebra"));
        Assert.Equal(new[] { a.Id, b.Id }.Order().ToList(), await IdsAsync("?levelTypeId=" + w.LevelOfSchool1)); // b has no level = every level
        Assert.Equal(new[] { b.Id, c.Id }.Order().ToList(), await IdsAsync("?levelTypeId=" + w.OtherLevel));
        Assert.Equal(3, (await IdsAsync("?type=Link")).Count);
        Assert.Empty(await IdsAsync("?type=Book"));
        var tags = await w.ReadAsync<List<string>>(await w.SendAsync(w.School1Admin, HttpMethod.Get, Routes.LibraryTagsOf(w.Tree.School1.Id)));
        Assert.Equal(["history", "math", "notes"], tags);
    }

    [Fact]
    public async Task Paging_orders_by_title_and_validates()
    {
        using var w = await LibraryTestWorld.CreateAsync(factory, "LB8");
        foreach (var title in new[] { "Charlie", "Alpha", "Bravo" })
        {
            await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, title));
        }

        var page = await w.ListAsync(w.School1Admin, "?page=2&pageSize=2");

        Assert.Equal(["Charlie"], page.Items.Select(r => r.TitleEn));
        Assert.Equal(3, page.Total);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.SendAsync(w.School1Admin, HttpMethod.Get, Routes.Library + "?pageSize=201")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.SendAsync(w.School1Admin, HttpMethod.Get, Routes.Library + "?page=0")).StatusCode);
    }

    [Fact]
    public async Task Invalid_input_is_400()
    {
        using var w = await LibraryTestWorld.CreateAsync(factory, "LB9");
        var tooManyTags = Enumerable.Range(0, 21).Select(i => "t" + i).ToArray();
        var unit = w.Tree.School1;
        var other = await AcademicTestKit.AddCourseAsync(factory, w.Tree.School2); // not allocated at School1

        foreach (var bad in new[]
        {
            Link(unit, titleEn: ""), Link(unit, titleEn: new string('t', 201)), Link(unit, url: "javascript:alert(1)"), Link(unit, url: "/relative"),
            Link(unit, url: null), Link(unit, tags: tooManyTags), Link(unit, tags: [new string('x', 51)]), Link(unit, type: "Nope"),
            Link(unit, levels: [Guid.NewGuid()]), Link(unit, courseId: other.Id), Link(unit, type: LibraryResourceTypes.Book), // a book needs a file
            Link(unit, type: LibraryResourceTypes.Book, fileAssetId: Guid.NewGuid()),
        })
        {
            var response = await w.SendAsync(w.School1Admin, HttpMethod.Post, Routes.Library, bad);
            Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (response.StatusCode, await CodeAsync(response)));
        }
    }

    [Fact]
    public async Task File_resource_needs_a_file_asset_of_its_own_unit_and_the_right_kind()
    {
        using var w = await LibraryTestWorld.CreateAsync(factory, "LB10");
        var pdfAtSchool1 = await w.UploadAssetAsync(w.Tree.School1);
        var pdfAtSchool2 = await w.UploadAssetAsync(w.Tree.School2);

        var ok = await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, "Book", type: LibraryResourceTypes.Book, fileAssetId: pdfAtSchool1));
        var wrongUnit = await w.SendAsync(w.Admin, HttpMethod.Post, Routes.Library, Link(w.Tree.School1, "Book", type: LibraryResourceTypes.Book, fileAssetId: pdfAtSchool2));
        var wrongKind = await w.SendAsync(w.School1Admin, HttpMethod.Post, Routes.Library, Link(w.Tree.School1, "Clip", type: LibraryResourceTypes.Video, fileAssetId: pdfAtSchool1));

        Assert.Equal(pdfAtSchool1, ok.File!.Id);
        Assert.Equal(w.Tree.School1.Id, ok.File.OrgUnitId);
        Assert.Null(ok.File.ClassCourseId);
        Assert.Equal(HttpStatusCode.BadRequest, wrongUnit.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongKind.StatusCode);
    }

    [Fact]
    public async Task Subject_course_must_be_available_at_the_resources_unit()
    {
        using var w = await LibraryTestWorld.CreateAsync(factory, "LB11");
        var atRoot = await AcademicTestKit.AddCourseAsync(factory, w.Tree.Root);
        await AcademicTestKit.AllocateAsync(factory, atRoot, w.Tree.Root); // allocated at the root: available below
        var siblingOnly = await AcademicTestKit.AddCourseAsync(factory, w.Tree.School2);
        await AcademicTestKit.AllocateAsync(factory, siblingOnly, w.Tree.School2);

        var inherited = await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, "Subject from above", courseId: atRoot.Id));
        var other = await w.SendAsync(w.School1Admin, HttpMethod.Post, Routes.Library, Link(w.Tree.School1, "Sibling subject", courseId: siblingOnly.Id));

        Assert.Equal(atRoot.Id, inherited.CourseId);
        Assert.False(string.IsNullOrEmpty(inherited.CourseNameEn));
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
    }

    [Fact]
    public async Task Reader_streams_the_file_and_download_is_refused_when_disabled()
    {
        using var w = await LibraryTestWorld.CreateAsync(factory, "LB12");
        var asset = await w.UploadAssetAsync(w.Tree.School1);
        var protectedBook = await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, "Protected", type: LibraryResourceTypes.Book, fileAssetId: asset, downloadAllowed: false));
        var openBook = await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, "Open", type: LibraryResourceTypes.Book, fileAssetId: asset, downloadAllowed: true));

        var reader = await w.SendAsync(w.Student, HttpMethod.Get, Routes.LibraryResourceReader(protectedBook.Id));
        var refused = await w.SendAsync(w.Student, HttpMethod.Get, Routes.LibraryResourceDownload(protectedBook.Id));
        var allowed = await w.SendAsync(w.Student, HttpMethod.Get, Routes.LibraryResourceDownload(openBook.Id));

        Assert.Equal(HttpStatusCode.OK, reader.StatusCode);
        Assert.Equal(LibraryTestWorld.Pdf, await reader.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("inline", reader.Content.Headers.ContentDisposition?.ToString() ?? string.Join(';', reader.Headers.GetValues("Content-Disposition")));
        Assert.Equal((HttpStatusCode.Forbidden, "download_disabled"), (refused.StatusCode, await CodeAsync(refused)));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.School2Admin, HttpMethod.Get, Routes.LibraryResourceReader(protectedBook.Id))).StatusCode);
    }

    [Fact]
    public async Task Link_resource_has_no_reader_and_a_student_of_another_level_cannot_read_a_file()
    {
        using var w = await LibraryTestWorld.CreateAsync(factory, "LB13");
        var link = await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, "A link"));
        var asset = await w.UploadAssetAsync(w.Tree.School1);
        var otherLevelBook = await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, "Other", type: LibraryResourceTypes.Book, fileAssetId: asset, levels: [w.OtherLevel]));

        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.Student, HttpMethod.Get, Routes.LibraryResourceReader(link.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.Student, HttpMethod.Get, Routes.LibraryResourceReader(otherLevelBook.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.Student, HttpMethod.Get, Routes.LibraryResourceDownload(otherLevelBook.Id))).StatusCode);
    }

    [Fact]
    public async Task Library_file_asset_is_readable_through_the_asset_route_only_while_downloads_are_allowed()
    {
        using var w = await LibraryTestWorld.CreateAsync(factory, "LB14");
        var open = await w.UploadAssetAsync(w.Tree.School1, "open.pdf");
        var closed = await w.UploadAssetAsync(w.Tree.School1, "closed.pdf");
        await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, "Open", type: LibraryResourceTypes.Book, fileAssetId: open, downloadAllowed: true));
        await w.CreateAsync(w.School1Admin, Link(w.Tree.School1, "Closed", type: LibraryResourceTypes.Book, fileAssetId: closed, downloadAllowed: false));

        Assert.Equal(HttpStatusCode.OK, (await w.SendAsync(w.Student, HttpMethod.Get, Routes.Asset(open))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.Student, HttpMethod.Get, Routes.Asset(closed))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.SendAsync(w.School1Admin, HttpMethod.Get, Routes.Asset(closed))).StatusCode); // Library.Manage at the owning unit
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.School2Admin, HttpMethod.Get, Routes.Asset(open))).StatusCode);
    }

    [Fact]
    public Task LibraryResource_is_isolated_per_client_and_subtree() => AssertIsolatedAsync(0, db => db.Set<LibraryResource>().Select(x => x.Id));

    [Fact]
    public Task LibraryTag_is_isolated_per_client_and_subtree() => AssertIsolatedAsync(1, db => db.Set<LibraryTag>().Select(x => x.Id));

    [Fact]
    public Task LibraryResourceLevel_is_isolated_per_client_and_subtree() => AssertIsolatedAsync(2, db => db.Set<LibraryResourceLevel>().Select(x => x.Id));

    /// <summary>One resource with a tag and a level in School 1 and School 2 of client A and in client B: each scope sees its subtree only.</summary>
    private async Task AssertIsolatedAsync(int index, Func<LmsDbContext, IQueryable<Guid>> table)
    {
        var a = await AccessTestData.SeedAsync(factory, "LI" + index + "A");
        var b = await AccessTestData.SeedAsync(factory, "LI" + index + "B");
        var inSchool1 = (await AddRowsAsync(a.School1))[index];
        var inSchool2 = (await AddRowsAsync(a.School2))[index];
        var otherClient = (await AddRowsAsync(b.School1))[index];
        Guid[] all = [inSchool1, inSchool2, otherClient];

        async Task<List<Guid>> VisibleAsync(TestCurrentContext who)
        {
            await using var db = factory.CreateDbContext(who);
            return (await table(db).ToListAsync()).Where(all.Contains).Order().ToList();
        }

        Assert.Equal(new[] { inSchool1, inSchool2 }.Order().ToList(), await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path)));
        Assert.Equal([inSchool1], await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.School1.Path)));
        Assert.Equal([inSchool2], await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.School2.Path)));
        Assert.Equal([otherClient], await VisibleAsync(TestCurrentContext.For(b.Org.Id, b.Root.Path)));
    }

    /// <summary>The ids of a resource, its tag and its level, in that order.</summary>
    private async Task<Guid[]> AddRowsAsync(Lms.Domain.Organizations.OrgUnit unit)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var resource = new LibraryResource(unit, "مورد", "Resource", null, null, LibraryResourceTypes.Link, null, null, null, "https://example.org", true);
        db.Add(resource);
        await db.SaveChangesAsync();
        var tag = new LibraryTag(resource, "t");
        var level = new LibraryResourceLevel(resource, unit.LevelTypeId);
        db.AddRange(tag, level);
        await db.SaveChangesAsync();
        return [resource.Id, tag.Id, level.Id];
    }
}
