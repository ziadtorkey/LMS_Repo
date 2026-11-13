using Lms.Client.Services;
using System.Globalization;
using Bunit;
using Lms.Client.Components.People;
using Lms.Client.Pages.Students;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.People;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Students;

public sealed class CreateStudentPageTests : StudentsTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private async Task<IRenderedComponent<CreateStudentPage>> RenderPageAsync(params string[] permissions)
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(permissions.Length == 0 ? [Permissions.Students.View, Permissions.Students.Create] : permissions);
        RenderProviders();
        return Render<CreateStudentPage>();
    }

    private void Type(IRenderedComponent<CreateStudentPage> page, string id, string value)
    {
        var input = page.Find($"input[data-testid={id}]");
        // Lookup fields are debounced (500 ms) and react to typing (oninput); the others to a committed change.
        if (id.StartsWith("guardian-mobile") || id.StartsWith("guardian-national"))
        {
            input.Input(value);
            // The debounce timer commits the value and runs the guardian lookup. Saving before it fires races it: under load the commit
            // can land in the middle of the save's validation and the click is silently dropped. Wait until the lookup has run.
            page.WaitForAssertion(() => Assert.Contains(GuardiansApi.Lookups, l => l.NationalId == value || l.Mobile == value), TimeSpan.FromSeconds(10));
        }
        else input.Change(value);
    }

    private static async Task ChooseUnitAsync(IRenderedComponent<CreateStudentPage> page) =>
        await page.InvokeAsync(() => SelectWithTestId(page, "student-home-unit").Instance.ValueChanged.InvokeAsync(FakeUnits.SchoolId));

    private async Task FillAsync(IRenderedComponent<CreateStudentPage> page, string nameEn = "Yara Nabil", DateTime? dob = null)
    {
        await ChooseUnitAsync(page);
        Type(page, "student-name-ar", "يارا نبيل");
        Type(page, "student-name-en", nameEn);
        await page.InvokeAsync(() => page.FindComponent<MudDatePicker>().Instance.DateChanged.InvokeAsync(dob ?? new DateTime(2015, 4, 2)));
        await page.InvokeAsync(() => StringSelect(page, "student-gender").Instance.ValueChanged.InvokeAsync(Genders.Female));
        await page.InvokeAsync(() => StringSelect(page, "student-religion").Instance.ValueChanged.InvokeAsync(Religions.Muslim));
        var nationality = page.FindComponents<MudAutocomplete<string>>().First(a => (string?)a.Instance.UserAttributes["data-testid"] == "student-nationality");
        await page.InvokeAsync(() => nationality.Instance.ValueChanged.InvokeAsync("EG"));
    }

    private static bool IsChecked(IRenderedComponent<MudCheckBox<bool>> box) => box.Find("input[type=checkbox]").HasAttribute("checked");

    private static async Task SaveAsync(IRenderedComponent<CreateStudentPage> page) =>
        await page.Find("[data-testid=student-save]").ClickAsync(new());

    /// <summary>Closes the credentials dialog (the page then navigates away) so no handler is left awaiting when the test ends.</summary>
    private async Task CloseCredentialsAsync()
    {
        var done = Dialogs.WaitForElement("[data-testid=credentials-done]", Wait);
        await done.ClickAsync(new());
        Dialogs.WaitForAssertion(() => Assert.Empty(Dialogs.FindAll("[data-testid=credentials-done]")), Wait);
    }

    /// <summary>For saves that end in a dialog: the click handler awaits the dialog result, so it must not be awaited.</summary>
    private static void SaveWithDialog(IRenderedComponent<CreateStudentPage> page) =>
        _ = page.Find("[data-testid=student-save]").ClickAsync(new());

    [Fact]
    public async Task Leaving_a_debounced_guardian_field_commits_it_at_once()
    {
        var page = await RenderPageAsync();
        await ChooseUnitAsync(page);
        var input = page.Find("input[data-testid=guardian-mobile-0]");

        input.Input("0508888888");
        Assert.Empty(GuardiansApi.Lookups); // still inside the 500 ms debounce
        await input.BlurAsync(new());

        // The browser blurs the focused field before a click or Tab reaches Save, which is why Save normally sees the typed value.
        page.WaitForAssertion(() => Assert.Contains(GuardiansApi.Lookups, l => l.Mobile == "0508888888"), TimeSpan.FromMilliseconds(250));
    }

    /// <summary>
    /// The real form-flush.js blurs the focused input, which commits a debounced field at once. Here the script's promise stays pending
    /// until the test has blurred the input itself, so what the save validates and sends depends on the flush having been awaited.
    /// </summary>
    private static async Task FlushLikeTheScriptDoesAsync(BunitJSInterop js, AngleSharp.Dom.IElement focused, Bunit.JSRuntimeInvocationHandler flush, Action waitForCall)
    {
        waitForCall();
        await focused.BlurAsync(new());
        flush.SetVoidResult();
    }

    [Fact]
    public async Task Saving_right_after_typing_a_guardian_mobile_sends_that_mobile()
    {
        var flush = JSInterop.SetupVoid("lmsFormFlush");
        var page = await RenderPageAsync();
        await FillAsync(page);
        Type(page, "guardian-name-ar-0", "ماجد");
        Type(page, "guardian-name-en-0", "Majed");
        var mobile = page.Find("input[data-testid=guardian-mobile-0]");
        mobile.Input("0509999999"); // inside the 500 ms debounce: the model has not seen it yet

        SaveWithDialog(page);
        await FlushLikeTheScriptDoesAsync(JSInterop, mobile, flush,
            () => page.WaitForAssertion(() => Assert.Contains(JSInterop.Invocations, i => i.Identifier == "lmsFormFlush"), Wait));

        page.WaitForAssertion(() => Assert.NotNull(Students.LastCreate), Wait);
        Assert.Equal("0509999999", Assert.Single(Students.LastCreate!.Guardians).NewGuardian!.Mobile);
        await CloseCredentialsAsync();
    }

    [Fact]
    public async Task Save_commits_pending_fields_first()
    {
        var page = await RenderPageAsync();
        await ChooseUnitAsync(page);

        await SaveAsync(page);

        Assert.Single(JSInterop.Invocations["lmsFormFlush"]);
    }

    [Fact]
    public async Task Code_field_hidden_in_auto_mode()
    {
        var page = await RenderPageAsync();

        await ChooseUnitAsync(page);

        Assert.Empty(page.FindAll("input[data-testid=student-code]"));
        Assert.NotNull(page.Find("[data-testid=student-code-auto]"));
    }

    [Fact]
    public async Task Code_field_is_required_in_manual_mode()
    {
        Settings.Values[SettingKeys.StudentsCodeMode.Name] = "\"Manual\"";
        var page = await RenderPageAsync();
        await FillAsync(page);
        page.WaitForElement("input[data-testid=student-code]");

        await SaveAsync(page);
        Assert.Empty(Students.Creates);

        Type(page, "student-code", "ST-77");
        SaveWithDialog(page);
        page.WaitForAssertion(() => Assert.Equal("ST-77", Students.LastCreate!.StudentCode));
        await CloseCredentialsAsync();
    }

    [Fact]
    public async Task Valid_form_posts_the_student_with_class_password_and_guardian()
    {
        var page = await RenderPageAsync();
        await FillAsync(page);
        await page.InvokeAsync(() => SelectWithTestId(page, "student-class").Instance.ValueChanged.InvokeAsync(FakeClasses.Grade1A.Id));
        Type(page, "student-password", "Welcome#2026");
        Type(page, "guardian-name-ar-0", "ماجد");
        Type(page, "guardian-name-en-0", "Majed");
        Type(page, "guardian-mobile-0", "0509999999");

        SaveWithDialog(page);

        page.WaitForAssertion(() => Assert.NotNull(Students.LastCreate));
        var sent = Students.LastCreate!;
        Assert.Equal("Yara Nabil", sent.Data.NameEn);
        Assert.Equal(new DateOnly(2015, 4, 2), sent.Data.DateOfBirth);
        Assert.Equal("EG", sent.Data.Nationality);
        Assert.Equal(FakeUnits.SchoolId, sent.HomeOrgUnitId);
        Assert.Equal(FakeClasses.Grade1A.Id, sent.ClassId);
        Assert.Equal("Welcome#2026", sent.DefaultPassword);
        Assert.Null(sent.StudentCode);
        var guardian = Assert.Single(sent.Guardians);
        Assert.Equal("Majed", guardian.NewGuardian!.NameEn);
        Assert.True(guardian.IsPrimary);
        await CloseCredentialsAsync();
    }

    [Fact]
    public async Task Required_fields_block_the_save()
    {
        var page = await RenderPageAsync();

        await SaveAsync(page);

        Assert.Empty(Students.Creates);
    }

    [Fact]
    public async Task Class_select_lists_classes_of_unit_and_year()
    {
        var page = await RenderPageAsync();

        await ChooseUnitAsync(page);

        Assert.Equal(FakeUnits.SchoolId, ClassesApi.LastQuery!.OrgUnitId);
        Assert.Equal(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IYearStore>(Services).SelectedYearId, ClassesApi.LastQuery.AcademicYearId);
        SelectWithTestId(page, "student-class").Find("div.mud-input-control").MouseDown();
        var labels = Popovers.FindComponents<MudSelectItem<Guid?>>().Select(i => i.Markup).ToList();
        Assert.Contains(labels, l => l.Contains("Grade 1 A (12/30)"));
        Assert.Contains(labels, l => l.Contains("Grade 1 B (2/2)"));
    }

    [Fact]
    public async Task Choosing_a_full_class_shows_a_warning()
    {
        var page = await RenderPageAsync();
        await ChooseUnitAsync(page);

        await page.InvokeAsync(() => SelectWithTestId(page, "student-class").Instance.ValueChanged.InvokeAsync(FakeClasses.Grade1B.Id));

        Assert.NotNull(page.Find("[data-testid=student-class-full]"));
    }

    [Fact]
    public async Task Typing_guardian_national_id_offers_link_existing()
    {
        var page = await RenderPageAsync();
        await FillAsync(page);

        page.Find("input[data-testid=guardian-national-id-0]").Input("29001010101");

        page.WaitForElement("[data-testid=guardian-match-0]", Wait);
        Assert.Contains("Mona", page.Find("[data-testid=guardian-match-0]").TextContent);
        Assert.Contains("+*********111", page.Find("[data-testid=guardian-match-0]").TextContent);
        await page.Find("[data-testid=guardian-link-0]").ClickAsync(new());
        page.WaitForElement("[data-testid=guardian-linked-0]");

        SaveWithDialog(page);

        page.WaitForAssertion(() => Assert.NotNull(Students.LastCreate));
        var link = Assert.Single(Students.LastCreate!.Guardians);
        Assert.Equal(FakeGuardiansApi.MonaId, link.ExistingGuardianId);
        Assert.Null(link.NewGuardian);
        await CloseCredentialsAsync();
    }

    [Fact]
    public async Task Guardian_lookup_without_a_match_offers_nothing()
    {
        var page = await RenderPageAsync();

        page.Find("input[data-testid=guardian-mobile-0]").Input("0507777777");

        page.WaitForAssertion(() => Assert.Contains(GuardiansApi.Lookups, l => l.Mobile == "0507777777"), Wait);
        Assert.Empty(page.FindAll("[data-testid=guardian-match-0]"));
    }

    [Fact]
    public async Task Only_one_primary_guardian_can_be_selected()
    {
        var page = await RenderPageAsync();
        await page.Find("[data-testid=guardian-add]").ClickAsync(new());
        var boxes = page.FindComponents<MudCheckBox<bool>>().Where(c => ((string?)c.Instance.UserAttributes["data-testid"])?.StartsWith("guardian-primary-") == true).ToList();
        Assert.Equal(2, boxes.Count);
        Assert.True(IsChecked(boxes[0]));

        await page.InvokeAsync(() => boxes[1].Instance.ValueChanged.InvokeAsync(true));

        page.WaitForAssertion(() =>
        {
            var now = page.FindComponents<MudCheckBox<bool>>().Where(c => ((string?)c.Instance.UserAttributes["data-testid"])?.StartsWith("guardian-primary-") == true).ToList();
            Assert.False(IsChecked(now[0]));
            Assert.True(IsChecked(now[1]));
        });
    }

    [Fact]
    public async Task Partial_guardian_row_blocks_save_and_flags_the_row()
    {
        var page = await RenderPageAsync();
        await FillAsync(page);
        Type(page, "guardian-name-en-0", "Majed");

        await SaveAsync(page);

        Assert.Empty(Students.Creates);
        page.WaitForElement("[data-testid=guardian-error-0]");
    }

    [Fact]
    public async Task Guardian_required_marker_follows_unit_setting()
    {
        var page = await RenderPageAsync();
        await ChooseUnitAsync(page);
        Assert.Empty(page.FindAll("[data-testid=guardians-required]"));

        Settings.Values[SettingKeys.GuardiansDataRequired.Name] = "true";
        await ChooseUnitAsync(page);
        await page.InvokeAsync(() => SelectWithTestId(page, "student-home-unit").Instance.ValueChanged.InvokeAsync(FakeUnits.GradeId));

        page.WaitForElement("[data-testid=guardians-required]");
        await FillAsync(page);
        await SaveAsync(page);
        Assert.Empty(Students.Creates);
        Assert.Contains("At least one guardian is required", page.Find("[data-testid=student-error]").TextContent);
    }

    [Fact]
    public async Task Possible_duplicate_confirm_resends_with_flag()
    {
        var page = await RenderPageAsync();
        await FillAsync(page, "Ahmed Saeed", new DateTime(2014, 3, 5));

        SaveWithDialog(page);
        var yes = Dialogs.WaitForElement("button.mud-message-box__yes-button", Wait);
        Assert.Contains("1 students with the same name", Dialogs.Markup);
        await yes.ClickAsync(new());

        page.WaitForAssertion(() => Assert.Equal(2, Students.Creates.Count), Wait);
        Assert.False(Students.Creates[0].ConfirmPossibleDuplicate);
        Assert.True(Students.Creates[1].ConfirmPossibleDuplicate);
        await CloseCredentialsAsync();
    }

    [Fact]
    public async Task Possible_duplicate_cancel_does_not_create()
    {
        var page = await RenderPageAsync();
        await FillAsync(page, "Ahmed Saeed", new DateTime(2014, 3, 5));

        SaveWithDialog(page);
        var cancel = Dialogs.WaitForElement("button.mud-message-box__cancel-button", Wait);
        await cancel.ClickAsync(new());

        Assert.Single(Students.Creates);
    }

    [Fact]
    public async Task Over_capacity_confirm_resends_with_the_flag()
    {
        var page = await RenderPageAsync();
        await FillAsync(page);
        await page.InvokeAsync(() => SelectWithTestId(page, "student-class").Instance.ValueChanged.InvokeAsync(FakeClasses.Grade1B.Id));

        SaveWithDialog(page);
        var yes = Dialogs.WaitForElement("button.mud-message-box__yes-button", Wait);
        Assert.Contains("class is full", Dialogs.Markup);
        await yes.ClickAsync(new());

        page.WaitForAssertion(() => Assert.Equal(2, Students.Creates.Count), Wait);
        Assert.False(Students.Creates[0].ConfirmOverCapacity);
        Assert.True(Students.Creates[1].ConfirmOverCapacity);
        await CloseCredentialsAsync();
    }

    [Fact]
    public async Task Over_capacity_cancel_does_not_create()
    {
        var page = await RenderPageAsync();
        await FillAsync(page);
        await page.InvokeAsync(() => SelectWithTestId(page, "student-class").Instance.ValueChanged.InvokeAsync(FakeClasses.Grade1B.Id));

        SaveWithDialog(page);
        var cancel = Dialogs.WaitForElement("button.mud-message-box__cancel-button", Wait);
        await cancel.ClickAsync(new());

        Assert.Single(Students.Creates);
    }

    [Fact]
    public async Task Nationality_select_searches_arabic_and_english_names()
    {
        var page = await RenderPageAsync();
        var nationality = page.FindComponents<MudAutocomplete<string>>().First(a => (string?)a.Instance.UserAttributes["data-testid"] == "student-nationality");

        var english = (await nationality.Instance.SearchFunc!("egyp", CancellationToken.None)!)!;
        var arabic = (await nationality.Instance.SearchFunc!("مصر", CancellationToken.None)!)!;

        Assert.Contains("EG", english);
        Assert.Contains("EG", arabic);
        Assert.DoesNotContain("SA", english);
    }

    [Fact]
    public async Task Credentials_dialog_shows_password_once_and_delivery()
    {
        var page = await RenderPageAsync();
        await FillAsync(page);
        Type(page, "guardian-name-ar-0", "ماجد");
        Type(page, "guardian-name-en-0", "Majed");
        Type(page, "guardian-mobile-0", "0509999999");

        SaveWithDialog(page);

        Dialogs.WaitForElement("[data-testid=credentials-password]", Wait);
        Assert.Equal("Gen#Pass-4821", Dialogs.Find("input[data-testid=credentials-password]").GetAttribute("value"));
        Assert.StartsWith("S-", Dialogs.Find("input[data-testid=credentials-code]").GetAttribute("value"));
        Assert.Contains("primary guardian", Dialogs.Find("[data-testid=credentials-delivery]").TextContent);
        Assert.NotNull(Dialogs.Find("[data-testid=credentials-once]"));
        await CloseCredentialsAsync();
    }

    [Fact]
    public async Task Credentials_dialog_ignores_escape_and_backdrop_so_the_one_time_password_is_not_lost()
    {
        var page = await RenderPageAsync();
        await FillAsync(page);
        Type(page, "guardian-name-ar-0", "ماجد");
        Type(page, "guardian-name-en-0", "Majed");
        Type(page, "guardian-mobile-0", "0509999999");

        SaveWithDialog(page);

        Dialogs.WaitForElement("[data-testid=credentials-password]", Wait);
        // The provider keeps the open dialogs; the options each was opened with are on its reference.
        var open = (System.Collections.IEnumerable)typeof(MudBlazor.MudDialogProvider)
            .GetField("_dialogs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(Dialogs.Instance)!;
        var reference = open.Cast<object>().Single();
        var options = (MudBlazor.DialogOptions)reference.GetType().GetProperty("Options")!.GetValue(reference)!;
        Assert.False(options.CloseOnEscapeKey);
        Assert.False(options.BackdropClick);
        await CloseCredentialsAsync();
    }

    [Fact]
    public async Task Credentials_dialog_tells_the_school_to_hand_them_over_when_no_email_was_sent()
    {
        var page = await RenderPageAsync();
        await FillAsync(page);

        SaveWithDialog(page);

        Dialogs.WaitForElement("[data-testid=credentials-delivery]", Wait);
        Assert.Contains("Hand the credentials", Dialogs.Find("[data-testid=credentials-delivery]").TextContent);
        await CloseCredentialsAsync();
    }

    [Fact]
    public async Task A_server_problem_is_shown_and_nothing_is_lost()
    {
        var page = await RenderPageAsync();
        await FillAsync(page);
        Students.CreateFailure = new ApiProblemException(409, "student_code_taken", null, null);

        await SaveAsync(page);

        page.WaitForAssertion(() => Assert.Contains("code", page.Find("[data-testid=student-error]").TextContent, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Yara Nabil", page.Find("input[data-testid=student-name-en]").GetAttribute("value"));
    }

    [Fact]
    public async Task Page_is_forbidden_without_Students_Create()
    {
        var page = await RenderPageAsync(Permissions.Students.View);

        page.WaitForElement("[data-testid=student-create-forbidden]");
        Assert.Empty(page.FindAll("[data-testid=student-save]"));
    }

    [Fact]
    public async Task Password_field_is_left_to_right_and_masked()
    {
        var page = await RenderPageAsync();

        var input = page.Find("input[data-testid=student-password]");
        Assert.Equal("password", input.GetAttribute("type"));
        Assert.Equal("ltr", input.GetAttribute("dir"));
    }

    [Fact]
    public async Task Create_page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.Students.View, Permissions.Students.Create);

        var layout = Render<Lms.Client.Layout.MainLayout>(p => p.Add(l => l.Body,
            (Microsoft.AspNetCore.Components.RenderFragment)(b => { b.OpenComponent<CreateStudentPage>(0); b.CloseComponent(); })));

        await layout.InvokeAsync(() => GrantAsync(Permissions.Students.View, Permissions.Students.Create));
        layout.WaitForElement("input[data-testid=student-name-ar]", Wait);
        Assert.Equal("rtl", layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains("طالب جديد", layout.Find("[data-testid=student-create-title]").TextContent);
        Assert.Equal("rtl", layout.Find("input[data-testid=student-name-ar]").GetAttribute("dir"));
        Assert.Equal("ltr", layout.Find("input[data-testid=student-name-en]").GetAttribute("dir"));
    }

    [Fact]
    public async Task A_role_without_Settings_View_can_still_create_a_student_with_default_settings()
    {
        Settings.Failure = new Lms.Client.Services.ApiForbiddenException();
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        var before = navigation.Uri;
        var page = await RenderPageAsync();

        await ChooseUnitAsync(page);

        page.WaitForElement("[data-testid=student-code-auto]", Wait); // the default: codes are generated
        Assert.Equal(before, navigation.Uri); // not sent to /forbidden
    }
}
