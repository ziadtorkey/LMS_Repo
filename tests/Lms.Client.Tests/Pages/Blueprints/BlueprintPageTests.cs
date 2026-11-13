using System.Globalization;
using Bunit;
using Lms.Client.Pages.Blueprints;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Blueprints;
using Lms.Shared.Configuration;
using Lms.Shared.Courses;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Blueprints;

public sealed class BlueprintPageTests : BlueprintsTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static string[] Editor => [Permissions.Courses.View, Permissions.Blueprints.View, Permissions.Blueprints.Edit, Permissions.Blueprints.Publish];
    private static string[] Viewer => [Permissions.Courses.View, Permissions.Blueprints.View];

    private async Task<IRenderedComponent<BlueprintPage>> RenderPageAsync(Guid courseId, params string[] permissions)
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(permissions);
        RenderProviders();
        var page = Render<BlueprintPage>(p => p.Add(x => x.Id, courseId));
        page.WaitForElement("[data-testid=blueprint-title]", Wait);
        return page;
    }

    private async Task<IRenderedComponent<BlueprintPage>> EditAsync(Guid courseId)
    {
        Blueprints.SeedPublished(courseId);
        var page = await RenderPageAsync(courseId, Editor);
        await page.Find("[data-testid=blueprint-edit]").ClickAsync(new());
        page.WaitForElement("[data-testid=blueprint-editor]", Wait);
        return page;
    }

    // ---- viewing ----

    [Fact]
    public async Task Viewer_sees_read_only_tree()
    {
        Blueprints.SeedPublished(MathId);

        var page = await RenderPageAsync(MathId, Viewer);

        page.WaitForElement("[data-testid=blueprint-readonly]", Wait);
        Assert.Contains("Lesson 1.1", page.Markup);
        Assert.Empty(page.FindAll("[data-testid=blueprint-edit]"));
        Assert.Empty(page.FindAll("[data-testid=blueprint-editor]"));
        Assert.Contains("Viewing version 1", page.Find("[data-testid=blueprint-status]").TextContent);
    }

    [Fact]
    public async Task Edit_needs_Blueprints_Edit_even_for_a_course_editor()
    {
        Blueprints.SeedPublished(MathId);

        var page = await RenderPageAsync(MathId, Permissions.Courses.View, Permissions.Blueprints.View);

        Assert.Empty(page.FindAll("[data-testid=blueprint-edit]"));
    }

    [Fact]
    public async Task Course_without_a_published_version_says_so()
    {
        var page = await RenderPageAsync(MathId, Viewer);

        page.WaitForElement("[data-testid=blueprint-none]", Wait);
    }

    [Fact]
    public async Task Section_label_follows_setting()
    {
        Blueprints.SeedPublished(MathId);
        Settings.Values[SettingKeys.BlueprintsSectionLabel.Name] = "\"Chapter\"";

        var page = await RenderPageAsync(MathId, Viewer);

        page.WaitForElement("[data-testid=blueprint-readonly]", Wait);
        Assert.Contains("Chapter 1", page.Find("[data-testid=section-0]").TextContent);
    }

    [Fact]
    public async Task Versions_panel_lists_versions_and_opens_an_old_one_read_only()
    {
        Blueprints.SeedPublished(MathId, 1);
        Blueprints.SeedPublished(MathId, 2);
        var page = await RenderPageAsync(MathId, Viewer);

        page.WaitForElement("[data-testid=version-view-1]", Wait);
        await page.Find("[data-testid=version-view-1]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Contains("Viewing version 1", page.Find("[data-testid=blueprint-status]").TextContent));
        Assert.Empty(page.FindAll("[data-testid=blueprint-editor]"));
    }

    // ---- editing ----

    [Fact]
    public async Task Edit_draft_creates_the_draft_and_opens_the_editor()
    {
        var page = await EditAsync(MathId);

        Assert.Equal(1, Blueprints.CreatedDrafts);
        Assert.Contains("Editing the draft", page.Find("[data-testid=blueprint-status]").TextContent);
        Assert.NotNull(page.Find("[data-testid=lesson-0-0]"));
    }

    [Fact]
    public async Task Move_buttons_reorder_without_mouse()
    {
        var page = await EditAsync(MathId);
        var first = page.Find("[data-testid=lesson-0-0]").TextContent;

        await page.Find("[data-testid=lesson-down-0-0]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Contains("Lesson 1.1", page.Find("[data-testid=lesson-0-1]").TextContent));
        Assert.Contains("Lesson 1.2", page.Find("[data-testid=lesson-0-0]").TextContent);
        Assert.Contains("Lesson 1.1", first);
        Assert.NotNull(page.Find("[data-testid=blueprint-unsaved]"));
    }

    [Fact]
    public async Task Drag_lesson_to_other_section_reorders_and_marks_dirty()
    {
        var page = await EditAsync(MathId);
        var tree = page.FindComponent<BlueprintTree>();
        var lesson = tree.Instance.Model.Sections[0].Lessons[0];

        await page.InvokeAsync(() => tree.Instance.OnDroppedAsync(new MudItemDropInfo<LessonRow>(lesson, "1", 2)));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid=blueprint-unsaved]")));
        Assert.Single(tree.Instance.Model.Sections[0].Lessons);
        Assert.Same(lesson, tree.Instance.Model.Sections[1].Lessons[2]);
    }

    [Fact]
    public async Task Lesson_dialog_edits_titles_objectives_and_planned_day()
    {
        var page = await EditAsync(MathId);

        _ = page.Find("[data-testid=lesson-edit-0-0]").ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=lesson-offset]", Wait);
        Dialogs.Find("input[data-testid=lesson-title-en]").Change("Fractions");
        Dialogs.Find("textarea[data-testid=lesson-objectives-en]").Change("Add fractions");
        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<MudNumericField<int?>>().Instance.ValueChanged.InvokeAsync(12));
        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Contains("Fractions", page.Find("[data-testid=lesson-0-0]").TextContent), Wait);
        var lesson = page.FindComponent<BlueprintTree>().Instance.Model.Sections[0].Lessons[0];
        Assert.Equal("Add fractions", lesson.ObjectivesEn);
        Assert.Equal(12, lesson.PlannedOffsetDays);
        Assert.NotNull(page.Find("[data-testid=blueprint-unsaved]"));
    }

    [Fact]
    public async Task Save_sends_the_whole_tree_with_the_revision_and_clears_the_unsaved_notice()
    {
        var page = await EditAsync(MathId);
        await page.Find("[data-testid=lesson-down-0-0]").ClickAsync(new());

        await page.Find("[data-testid=blueprint-save]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.NotNull(Blueprints.LastSave));
        Assert.Equal(1, Blueprints.LastSave!.Revision);
        Assert.Equal(2, Blueprints.LastSave.Sections.Count);
        Assert.Equal("Lesson 1.2", Blueprints.LastSave.Sections[0].Lessons[0].TitleEn);
        Assert.All(Blueprints.LastSave.Sections, s => Assert.NotNull(s.Key));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=blueprint-unsaved]")));
    }

    [Fact]
    public async Task Stale_save_shows_reload_and_reload_brings_back_the_server_draft()
    {
        var page = await EditAsync(MathId);
        await page.Find("[data-testid=lesson-down-0-0]").ClickAsync(new());
        Blueprints.SaveFailure = new ApiProblemException(409, "blueprint_stale", null, null);

        await page.Find("[data-testid=blueprint-save]").ClickAsync(new());
        page.WaitForElement("[data-testid=blueprint-stale]", Wait);

        await page.Find("[data-testid=blueprint-reload]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=blueprint-stale]")));
        Assert.Contains("Lesson 1.1", page.Find("[data-testid=lesson-0-0]").TextContent);
        Assert.Empty(page.FindAll("[data-testid=blueprint-unsaved]"));
    }

    [Fact]
    public async Task Save_is_blocked_while_a_title_is_missing()
    {
        var page = await EditAsync(MathId);
        await page.Find("[data-testid=section-add]").ClickAsync(new());

        await page.Find("[data-testid=blueprint-save]").ClickAsync(new());

        page.WaitForElement("[data-testid=blueprint-validation]", Wait);
        Assert.Null(Blueprints.LastSave);
    }

    [Fact]
    public async Task Publish_is_disabled_while_there_are_unsaved_changes()
    {
        var page = await EditAsync(MathId);
        Assert.False(page.Find("[data-testid=blueprint-publish]").HasAttribute("disabled"));

        await page.Find("[data-testid=lesson-down-0-0]").ClickAsync(new());

        Assert.True(page.Find("[data-testid=blueprint-publish]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Publish_button_needs_Blueprints_Publish()
    {
        Blueprints.SeedPublished(MathId);
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Courses.View, Permissions.Blueprints.View, Permissions.Blueprints.Edit);
        RenderProviders();
        var page = Render<BlueprintPage>(p => p.Add(x => x.Id, MathId));
        page.WaitForElement("[data-testid=blueprint-edit]", Wait);
        await page.Find("[data-testid=blueprint-edit]").ClickAsync(new());
        page.WaitForElement("[data-testid=blueprint-editor]", Wait);

        Assert.Empty(page.FindAll("[data-testid=blueprint-publish]"));
    }

    // ---- publish ----

    [Fact]
    public async Task Publish_requires_apply_mode_choice()
    {
        var page = await EditAsync(MathId);

        _ = page.Find("[data-testid=blueprint-publish]").ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=publish-confirm]", Wait);
        await Dialogs.Find("[data-testid=publish-confirm]").ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=publish-mode-required]", Wait);
        Assert.Null(Blueprints.LastPublish);

        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<MudRadioGroup<string>>().Instance.ValueChanged.InvokeAsync(BlueprintApplyModes.PushToExisting));
        await Dialogs.Find("[data-testid=publish-confirm]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.NotNull(Blueprints.LastPublish), Wait);
        Assert.Equal(BlueprintApplyModes.PushToExisting, Blueprints.LastPublish!.ApplyMode);
        page.WaitForAssertion(() => Assert.Contains("4 class plans", page.Find("[data-testid=blueprint-message]").TextContent), Wait);
        Assert.Contains("Viewing version 2", page.Find("[data-testid=blueprint-status]").TextContent);
    }

    [Fact]
    public async Task New_classes_only_publish_does_not_report_queued_classes()
    {
        var page = await EditAsync(MathId);

        _ = page.Find("[data-testid=blueprint-publish]").ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=publish-confirm]", Wait);
        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<MudRadioGroup<string>>().Instance.ValueChanged.InvokeAsync(BlueprintApplyModes.NewClassesOnly));
        await Dialogs.Find("[data-testid=publish-confirm]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Contains("classes created from now on", page.Find("[data-testid=blueprint-message]").TextContent), Wait);
    }

    // ---- copy ----

    [Fact]
    public async Task Copy_replaces_the_draft_with_the_chosen_course_version()
    {
        var science = Courses.All.First(c => c.Summary.Code == "SCI101").Summary;
        Blueprints.SeedPublished(science.Id);
        var page = await EditAsync(MathId);

        _ = page.Find("[data-testid=blueprint-copy]").ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=copy-course]", Wait);
        var search = Dialogs.FindComponent<MudAutocomplete<CourseSummaryDto>>();
        await Dialogs.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync(science));
        Dialogs.WaitForElement("[data-testid=copy-confirm]", Wait);
        Dialogs.WaitForAssertion(() => Assert.False(Dialogs.Find("[data-testid=copy-confirm]").HasAttribute("disabled")), Wait);
        await Dialogs.Find("[data-testid=copy-confirm]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.NotNull(Blueprints.LastCopy), Wait);
        Assert.Equal(science.Id, Blueprints.LastCopy!.SourceCourseId);
        Assert.Equal(1, Blueprints.LastCopy.SourceVersion);
    }

    [Fact]
    public async Task Copy_does_not_offer_the_course_itself()
    {
        var page = await EditAsync(MathId);

        _ = page.Find("[data-testid=blueprint-copy]").ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=copy-course]", Wait);
        var search = Dialogs.FindComponent<MudAutocomplete<CourseSummaryDto>>();
        var found = (await search.Instance.SearchFunc!("", CancellationToken.None)!)!.ToList();

        Assert.DoesNotContain(found, c => c.Id == MathId);
        Assert.NotEmpty(found);
    }

    // ---- direction ----

    [Fact]
    public async Task Builder_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        Blueprints.SeedPublished(MathId);
        var id = MathId;

        var layout = Render<Lms.Client.Layout.MainLayout>(p => p.Add(l => l.Body,
            (Microsoft.AspNetCore.Components.RenderFragment)(b =>
            {
                b.OpenComponent<BlueprintPage>(0);
                b.AddAttribute(1, nameof(BlueprintPage.Id), id);
                b.CloseComponent();
            })));
        await layout.InvokeAsync(() => GrantAsync(Editor));

        layout.WaitForElement("[data-testid=blueprint-readonly]", Wait);
        Assert.Equal("rtl", layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains("الأسبوع 1", layout.Find("[data-testid=section-0]").TextContent);
        Assert.Contains("الدرس 1.1", layout.Find("[data-testid=lesson-0-0]").TextContent);
    }
}
