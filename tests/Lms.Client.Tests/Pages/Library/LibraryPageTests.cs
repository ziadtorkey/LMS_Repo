using System.Globalization;
using Bunit;
using Lms.Client.Pages.Library;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Lms.Shared.Library;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Library;

public sealed class LibraryPageTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly FakeLibraryApi _library = new();

    public LibraryPageTests()
    {
        Services.AddSingleton<ILibraryApi>(_library);
        Services.AddSingleton<IAssetsApi, FakeAssetsApi>();
        Services.AddSingleton<ICoursesApi, FakeCoursesApi>();
        Culture.Current = new CultureInfo("en");
    }

    private async Task<IRenderedComponent<LibraryPage>> OpenAsync(params string[] permissions)
    {
        await GrantAsync(permissions);
        RenderProviders();
        var page = Render<LibraryPage>();
        page.WaitForElement("[data-testid=library-filters], [data-testid=library-forbidden]", Wait);
        return page;
    }

    private static AssetDto File(string name = "book.pdf") =>
        new(Guid.NewGuid(), null, AssetKinds.Document, name, "application/pdf", 10, null, true, DateTime.UtcNow, FakeLibraryApi.UnitId);

    [Fact]
    public async Task Fake_download_of_a_reader_only_resource_is_refused_with_the_servers_code()
    {
        var readerOnly = _library.Add("Reader only", downloadAllowed: false);

        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => _library.DownloadAsync(readerOnly.Id));

        Assert.Equal(403, ex.Status);
        Assert.Equal(ProblemCodes.download_disabled, ex.Code);
    }

    [Fact]
    public async Task Without_Library_View_the_page_is_forbidden()
    {
        var page = await OpenAsync();

        Assert.NotNull(page.Find("[data-testid=library-forbidden]"));
    }

    [Fact]
    public async Task Cards_show_inherited_resources_read_only()
    {
        var own = _library.Add("Own book");
        var inherited = _library.Add("Group book") with { IsInherited = true, CanEdit = false };
        _library.Resources[1] = inherited;

        var page = await OpenAsync(Permissions.Library.View, Permissions.Library.Manage);

        page.WaitForElement($"[data-testid=library-card-{own.Id}]", Wait);
        Assert.NotNull(page.Find($"[data-testid=library-edit-{own.Id}]"));
        Assert.NotNull(page.Find($"[data-testid=library-inherited-{inherited.Id}]"));
        Assert.Empty(page.FindAll($"[data-testid=library-edit-{inherited.Id}]"));
        Assert.Empty(page.FindAll($"[data-testid=library-delete-{inherited.Id}]"));
    }

    [Fact]
    public async Task Students_see_no_manage_or_add_buttons()
    {
        var r = _library.Add("Book");

        var page = await OpenAsync(Permissions.Library.View);

        page.WaitForElement($"[data-testid=library-card-{r.Id}]", Wait);
        Assert.Empty(page.FindAll("[data-testid=library-new]"));
        Assert.Empty(page.FindAll($"[data-testid=library-edit-{r.Id}]"));
        Assert.Empty(page.FindAll($"[data-testid=library-add-{r.Id}]"));
    }

    [Fact]
    public async Task Filters_compose()
    {
        var grade1 = Guid.NewGuid();
        var algebraVideo = _library.Add("Algebra video", LibraryResourceTypes.Video, [grade1], tags: ["algebra"]);
        _library.Add("Algebra book", LibraryResourceTypes.Book, [grade1], tags: ["algebra"]);
        _library.Add("Geometry video", LibraryResourceTypes.Video, [grade1], tags: ["geometry"]);
        _library.Add("Other level video", LibraryResourceTypes.Video, [Guid.NewGuid()], tags: ["algebra"]);
        Org.Levels.Add(new Lms.Shared.Organizations.LevelTypeDto(grade1, "الصف الأول", "Grade 1", 1, true, 1));
        var page = await OpenAsync(Permissions.Library.View, Permissions.LevelTypes.View);
        page.WaitForElement("[data-testid=library-grid]", Wait);

        await page.InvokeAsync(() => page.FindComponent<MudSelect<string>>().Instance.ValueChanged.InvokeAsync(LibraryResourceTypes.Video));
        await page.InvokeAsync(() => page.FindComponents<MudSelect<string>>().Last().Instance.ValueChanged.InvokeAsync("algebra"));
        await page.InvokeAsync(() => page.FindComponents<MudSelect<Guid?>>().First().Instance.ValueChanged.InvokeAsync(grade1));

        page.WaitForAssertion(() =>
        {
            var last = _library.Queries.Last();
            Assert.Equal((LibraryResourceTypes.Video, "algebra", (Guid?)grade1), (last.Type, last.Tag, last.LevelTypeId));
            var ids = page.FindAll("[data-testid^=library-card-]").Select(c => c.GetAttribute("data-testid")!["library-card-".Length..]).ToList();
            Assert.Equal([algebraVideo.Id.ToString()], ids);
        });
    }

    [Fact]
    public async Task Search_sends_the_text()
    {
        _library.Add("Fractions");
        _library.Add("Verbs");
        var page = await OpenAsync(Permissions.Library.View);
        page.WaitForElement("[data-testid=library-grid]", Wait);

        page.Find("[data-testid=library-search]").Input("frac");

        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-testid^=library-card-]")));
        Assert.Equal("frac", _library.Queries.Last().Search);
    }

    [Fact]
    public async Task Reader_only_resource_is_read_through_the_protected_reader_as_a_blob_url()
    {
        JSInterop.Setup<string>("lmsBlobUrl", _ => true).SetResult("blob:reader-test");
        JSInterop.SetupVoid("lmsRevokeBlobUrl", _ => true);
        var locked = _library.Add("Reader only", downloadAllowed: false, file: File());
        var page = await OpenAsync(Permissions.Library.View);
        page.WaitForElement($"[data-testid=library-open-{locked.Id}]", Wait);

        page.Find($"[data-testid=library-open-{locked.Id}]").Click();

        Dialogs.WaitForAssertion(() => Assert.Equal("blob:reader-test", Dialogs.Find("[data-testid=library-reader-pdf]").GetAttribute("src")));
        Assert.Equal([locked.Id], _library.Reads);
    }

    [Fact]
    public async Task Download_button_hidden_when_disabled()
    {
        var locked = _library.Add("Reader only", downloadAllowed: false, file: File());
        var page = await OpenAsync(Permissions.Library.View);
        page.WaitForElement($"[data-testid=library-open-{locked.Id}]", Wait);

        page.Find($"[data-testid=library-open-{locked.Id}]").Click();

        Dialogs.WaitForAssertion(() => Assert.NotNull(Dialogs.Find("[data-testid=library-reader]")));
        Assert.Empty(Dialogs.FindAll("[data-testid=library-download]"));
        Assert.Empty(Dialogs.FindAll("[data-testid=asset-download]"));
    }

    [Fact]
    public async Task Download_button_shown_when_allowed()
    {
        var open = _library.Add("Downloadable", downloadAllowed: true, file: File());
        var page = await OpenAsync(Permissions.Library.View);
        page.WaitForElement($"[data-testid=library-open-{open.Id}]", Wait);

        page.Find($"[data-testid=library-open-{open.Id}]").Click();

        Dialogs.WaitForAssertion(() => Assert.NotNull(Dialogs.Find("[data-testid=library-download]")));
    }

    [Fact]
    public async Task Add_to_lesson_creates_module_reference()
    {
        var resource = _library.Add("Reference book", file: File());
        var page = await OpenAsync(Permissions.Library.View, Permissions.Modules.Manage, Permissions.LessonPlans.View);
        page.WaitForElement($"[data-testid=library-add-{resource.Id}]", Wait);
        var cc = FakeLessonPlansApi.ArabicClassCourseId;
        var lesson = Plans.Plans[cc].Sections.First().Lessons.First();

        page.Find($"[data-testid=library-add-{resource.Id}]").Click();
        Dialogs.WaitForAssertion(() => Assert.NotNull(Dialogs.Find("[data-testid=add-lesson-save]")));
        await Dialogs.InvokeAsync(() => Dialogs.FindComponents<MudSelect<Guid?>>().First(s => s.Instance.Label == "Class course").Instance.ValueChanged.InvokeAsync(cc));
        Dialogs.WaitForAssertion(() => Assert.False(Dialogs.FindComponents<MudSelect<Guid?>>().First(s => s.Instance.Label == "Lesson").Instance.Disabled));
        await Dialogs.InvokeAsync(() => Dialogs.FindComponents<MudSelect<Guid?>>().First(s => s.Instance.Label == "Lesson").Instance.ValueChanged.InvokeAsync(lesson.Key));
        Dialogs.WaitForAssertion(() => Assert.False(Dialogs.Find("[data-testid=add-lesson-save]").HasAttribute("disabled")));

        Dialogs.Find("[data-testid=add-lesson-save]").Click();

        Dialogs.WaitForAssertion(() => Assert.Single(_library.Added));
        var (id, request) = _library.Added.Single();
        Assert.Equal((resource.Id, cc, lesson.Key), (id, request.ClassCourseId, request.LessonKey));
        Assert.Equal("Reference book", request.Module.TitleEn);
    }

    [Fact]
    public async Task Manager_creates_a_link_resource_and_a_blocked_link_is_refused()
    {
        var page = await OpenAsync(Permissions.Library.View, Permissions.Library.Manage);
        page.WaitForElement("[data-testid=library-new]", Wait);

        page.Find("[data-testid=library-new]").Click();
        Dialogs.WaitForAssertion(() => Assert.NotNull(Dialogs.Find("[data-testid=library-save]")));
        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<Lms.Client.Components.People.UnitPicker>().Instance.ValueChanged.InvokeAsync(FakeLibraryApi.UnitId));
        Dialogs.Find("[data-testid=library-title-ar]").Change("موقع");
        Dialogs.Find("[data-testid=library-title-en]").Change("Site");
        await Dialogs.InvokeAsync(() => Dialogs.FindComponents<MudSelect<string>>().First(x => x.Instance.Label == "Type").Instance.ValueChanged.InvokeAsync(LibraryResourceTypes.Link));
        Dialogs.WaitForAssertion(() => Assert.NotNull(Dialogs.Find("[data-testid=library-form-url]")));

        Dialogs.Find("[data-testid=library-form-url]").Change("javascript:alert(1)");
        Dialogs.Find("[data-testid=library-save]").Click();
        Dialogs.WaitForAssertion(() => Assert.NotNull(Dialogs.Find("[data-testid=library-form-error]")));
        Assert.Empty(_library.Resources);

        Dialogs.Find("[data-testid=library-form-url]").Change("https://example.test");
        Dialogs.Find("[data-testid=library-form-tags]").Change("math, algebra");
        Dialogs.Find("[data-testid=library-save]").Click();

        Dialogs.WaitForAssertion(() => Assert.Single(_library.Resources));
        var saved = _library.Resources.Single();
        Assert.Equal(("Site", LibraryResourceTypes.Link, "https://example.test"), (saved.TitleEn, saved.Type, saved.Url));
        Assert.Equal(["math", "algebra"], saved.Tags);
    }

    [Fact]
    public async Task Callers_without_lookup_permissions_still_open_the_library()
    {
        // Students and teachers cannot read level types or courses: a 403 there would send the whole page to /forbidden.
        Org.LevelsFailure = new Lms.Client.Services.ApiForbiddenException();
        _library.Add("Book");
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        var before = navigation.Uri;

        var page = await OpenAsync(Permissions.Library.View);

        page.WaitForElement("[data-testid=library-grid]", Wait);
        Assert.Equal(before, navigation.Uri);
        Assert.Empty(page.FindAll("[data-testid=library-level]"));
        Assert.Empty(page.FindAll("[data-testid=library-course]"));
    }
}
