using System.Globalization;
using Bunit;
using Lms.Client.Components.People;
using Lms.Client.Pages.Classes;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Lms.Shared.People;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Classes;

public sealed class ClassDialogsTests : ClassesTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static string[] Admin => [Permissions.Classes.View, Permissions.Classes.Create, Permissions.Classes.Update, Permissions.Classes.Close];

    private async Task<IRenderedComponent<ClassListPage>> OpenAsync(string buttonTestId)
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Admin);
        RenderProviders();
        var page = Render<ClassListPage>();
        page.WaitForElement($"[data-testid={buttonTestId}]", Wait);
        // The click handler awaits the dialog result, so do not await the click itself.
        _ = page.Find($"[data-testid={buttonTestId}]").ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=dialog-cancel]", Wait);
        return page;
    }

    private void Type(string id, string value) => Dialogs.Find($"input[data-testid={id}]").Change(value);

    private async Task FillClassAsync(string code = "2A")
    {
        await Dialogs.InvokeAsync(() => SelectWithTestId(Dialogs, "class-unit").Instance.ValueChanged.InvokeAsync(FakeUnits.GradeId));
        Type("class-code", code);
        Type("class-name-ar", "الصف الثاني أ");
        Type("class-name-en", "Grade 2 A");
    }

    // ---- class dialog ----

    [Fact]
    public async Task Unit_picker_offers_only_class_hosting_units()
    {
        await OpenAsync("class-new");
        Dialogs.WaitForAssertion(() => Assert.NotNull(Dialogs.Find("[data-testid=class-unit]")), Wait);

        var picker = Dialogs.FindComponents<UnitPicker>().First(u => u.Instance.TestId == "class-unit");
        picker.Find("div.mud-input-control").MouseDown();

        var offered = Popovers.FindComponents<MudSelectItem<Guid?>>().Select(i => i.Instance.Value).ToList();
        Assert.Equal([(Guid?)FakeUnits.GradeId], offered);
    }

    [Fact]
    public async Task Valid_class_is_posted_for_the_selected_year()
    {
        await OpenAsync("class-new");
        await FillClassAsync();
        await Dialogs.Find("input[data-testid=class-capacity]").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "25" });

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Dialogs.WaitForAssertion(() => Assert.NotNull(ClassesApi.LastSave), Wait);
        var sent = ClassesApi.LastSave!;
        Assert.Equal("2A", sent.Code);
        Assert.Equal(FakeUnits.GradeId, sent.OrgUnitId);
        Assert.Equal(YearId, sent.AcademicYearId);
        Assert.Equal(25, sent.Capacity);
        Assert.Null(sent.HomeroomTeacherUserId);
    }

    [Fact]
    public async Task Required_fields_block_the_save()
    {
        await OpenAsync("class-new");

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Assert.Null(ClassesApi.LastSave);
    }

    [Fact]
    public async Task Code_taken_shows_the_field_message()
    {
        await OpenAsync("class-new");
        await FillClassAsync("1A");
        // 1A exists in the seeded classes but for another year id: make the year match.
        var existing = ClassesApi.All.First(c => c.Code == "1A");
        await ClassesApi.UpdateAsync(existing.Id, new SaveClassRequest("1A", existing.NameAr, existing.NameEn, existing.OrgUnitId, YearId, existing.Capacity, null));

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Dialogs.WaitForAssertion(() => Assert.Contains("code", Dialogs.Markup, StringComparison.OrdinalIgnoreCase), Wait);
    }

    [Fact]
    public async Task Homeroom_search_offers_teachers_only()
    {
        await OpenAsync("class-new");
        var teacherRole = Access.Roles.First(r => r.BuiltInKey == BuiltInRoles.Teacher).Id;
        var search = Dialogs.FindComponent<MudAutocomplete<StaffSummaryDto>>();

        _ = await search.Instance.SearchFunc!("sa", CancellationToken.None)!;

        var query = Staff.LastQuery!;
        Assert.Equal(teacherRole, query.RoleId);
        Assert.Equal(PersonStatuses.Active, query.Status);
    }

    [Fact]
    public async Task Editing_keeps_the_unit_read_only_and_sends_an_update()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Admin);
        RenderProviders();
        var page = Render<ClassListPage>();
        _ = page.WaitForElement($"[data-testid=class-edit-{FakeClasses.Grade1A.Id}]", Wait).ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=class-code]", Wait);

        Assert.True(SelectWithTestId(Dialogs, "class-unit").Instance.ReadOnly);
        Type("class-name-en", "Grade 1 Alpha");
        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Dialogs.WaitForAssertion(() => Assert.Equal(FakeClasses.Grade1A.Id, ClassesApi.LastUpdatedId), Wait);
        Assert.Equal("Grade 1 Alpha", ClassesApi.LastSave!.NameEn);
    }

    // ---- bulk dialog ----

    [Fact]
    public async Task Bulk_preview_renders_names_from_pattern()
    {
        await OpenAsync("class-bulk");

        Type("bulk-pattern-ar", "الصف {N}");
        Type("bulk-pattern-en", "Class {N}");
        Type("bulk-pattern-code", "C{N}");

        Dialogs.WaitForAssertion(() =>
        {
            var preview = Dialogs.Find("[data-testid=bulk-preview]").TextContent;
            Assert.Contains("C1", preview);
            Assert.Contains("Class 1", preview);
            Assert.Contains("C5", preview);
        }, Wait);
        Assert.Contains("5 classes will be created", Dialogs.Find("[data-testid=bulk-count]").TextContent);
    }

    [Fact]
    public async Task Bulk_pattern_without_the_number_placeholder_is_a_problem()
    {
        await OpenAsync("class-bulk");

        Type("bulk-pattern-ar", "الصف");
        Type("bulk-pattern-en", "Class {N}");
        Type("bulk-pattern-code", "C{N}");

        Dialogs.WaitForElement("[data-testid=bulk-problem]", Wait);
        Assert.True(Dialogs.Find("[data-testid=dialog-save]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Bulk_range_over_fifty_is_a_problem()
    {
        await OpenAsync("class-bulk");
        Type("bulk-pattern-ar", "الصف {N}"); Type("bulk-pattern-en", "Class {N}"); Type("bulk-pattern-code", "C{N}");

        await Dialogs.InvokeAsync(() => Dialogs.FindComponents<MudNumericField<int>>().First(n => (string?)n.Instance.UserAttributes["data-testid"] == "bulk-to").Instance.ValueChanged.InvokeAsync(60));

        Dialogs.WaitForElement("[data-testid=bulk-problem]", Wait);
        Assert.Contains("At most 50", Dialogs.Find("[data-testid=bulk-problem]").TextContent);
    }

    [Fact]
    public async Task Bulk_create_sends_the_request()
    {
        await OpenAsync("class-bulk");
        await Dialogs.InvokeAsync(() => SelectWithTestId(Dialogs, "bulk-unit").Instance.ValueChanged.InvokeAsync(FakeUnits.GradeId));
        Type("bulk-pattern-ar", "الصف {N}"); Type("bulk-pattern-en", "Class {N}"); Type("bulk-pattern-code", "BC{N}");

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Dialogs.WaitForAssertion(() => Assert.NotNull(ClassesApi.LastBulk), Wait);
        var sent = ClassesApi.LastBulk!;
        Assert.Equal(FakeUnits.GradeId, sent.OrgUnitId);
        Assert.Equal(YearId, sent.AcademicYearId);
        Assert.Equal((1, 5), (sent.From, sent.To));
        Assert.Equal("BC{N}", sent.CodePattern);
    }

    [Fact]
    public async Task Bulk_code_conflict_is_shown_and_nothing_is_closed()
    {
        await OpenAsync("class-bulk");
        await Dialogs.InvokeAsync(() => SelectWithTestId(Dialogs, "bulk-unit").Instance.ValueChanged.InvokeAsync(FakeUnits.GradeId));
        ClassesApi.WriteFailure = new ApiProblemException(409, "class_code_taken", null, null);
        Type("bulk-pattern-ar", "الصف {N}"); Type("bulk-pattern-en", "Class {N}"); Type("bulk-pattern-code", "BC{N}");

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Dialogs.WaitForElement("[data-testid=dialog-error]", Wait);
    }

    // ---- copy dialog ----

    [Fact]
    public async Task Copy_sends_years_unit_and_replace_text_and_shows_skipped_codes()
    {
        ClassesApi.CopySkipped.AddRange(["1B", "1C"]);
        await OpenAsync("class-copy");
        await Dialogs.InvokeAsync(() => SelectWithTestId(Dialogs, "copy-unit").Instance.ValueChanged.InvokeAsync(FakeUnits.SchoolId));
        await Dialogs.InvokeAsync(() => SelectWithTestId(Dialogs, "copy-target-year").Instance.ValueChanged.InvokeAsync(NextYearId));
        Type("copy-replace-from", "2026");
        Type("copy-replace-to", "2027");

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Dialogs.WaitForElement("[data-testid=copy-skipped]", Wait);
        Assert.Contains("1B, 1C", Dialogs.Find("[data-testid=copy-skipped]").TextContent);
        var sent = ClassesApi.LastCopy!;
        Assert.Equal(YearId, sent.SourceYearId);
        Assert.Equal(NextYearId, sent.TargetYearId);
        Assert.Equal(FakeUnits.SchoolId, sent.OrgUnitId);
        Assert.True(sent.IncludeCourses);
        Assert.Equal(("2026", "2027"), (sent.ReplaceFrom, sent.ReplaceTo));
    }

    [Fact]
    public async Task Copy_needs_a_different_target_year()
    {
        await OpenAsync("class-copy");
        await Dialogs.InvokeAsync(() => SelectWithTestId(Dialogs, "copy-unit").Instance.ValueChanged.InvokeAsync(FakeUnits.SchoolId));

        Assert.True(Dialogs.Find("[data-testid=dialog-save]").HasAttribute("disabled"));

        await Dialogs.InvokeAsync(() => SelectWithTestId(Dialogs, "copy-target-year").Instance.ValueChanged.InvokeAsync(YearId));
        Assert.True(Dialogs.Find("[data-testid=dialog-save]").HasAttribute("disabled"));
        Assert.Null(ClassesApi.LastCopy);
    }
}
