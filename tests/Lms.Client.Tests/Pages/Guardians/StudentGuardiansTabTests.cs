using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Lms.Client.Pages.Students;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Configuration;
using Lms.Shared.People;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Guardians;

public sealed class StudentGuardiansTabTests : StudentsTestContext
{
    private static readonly Guid PrimaryId = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    private static readonly Guid SecondId = Guid.Parse("00000000-0000-0000-0000-0000000000e2");
    private IRenderedComponent<MudSnackbarProvider> _snackbars = default!;
    private int _changed;

    private StudentDetailDto StudentWithGuardians => Students.All[0] with
    {
        Guardians =
        [
            new(PrimaryId, "منى", "Mona", GuardianRelations.Mother, true, "0501111111", "mona@example.com", null, true, 1),
            new(SecondId, "خالد", "Khaled", GuardianRelations.Father, false, "0502222222", null, null, false, 0),
        ],
    };

    private IRenderedComponent<StudentGuardiansTab> RenderTab(bool canEdit = true, StudentDetailDto? student = null)
    {
        RenderProviders();
        _snackbars = Render<MudSnackbarProvider>();
        return Render<StudentGuardiansTab>(p => p
            .Add(t => t.Student, student ?? StudentWithGuardians)
            .Add(t => t.CanEdit, canEdit)
            .Add(t => t.OnChanged, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => _changed++)));
    }

    [Fact]
    public void Tab_lists_guardians_with_primary_and_portal_state()
    {
        Culture.Current = new CultureInfo("en");
        var cut = RenderTab();

        cut.WaitForElement($"[data-testid=guardian-primary-{PrimaryId}]");
        Assert.Empty(cut.FindAll($"[data-testid=guardian-primary-{SecondId}]"));
        Assert.Contains("Portal account active", cut.Find($"[data-testid=guardian-portal-{PrimaryId}]").TextContent);
        Assert.Contains("No portal account", cut.Find($"[data-testid=guardian-portal-{SecondId}]").TextContent);
        Assert.Contains("Also guardian of 1 other students", cut.Find($"[data-testid=guardian-others-{PrimaryId}]").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid=guardian-make-primary-{PrimaryId}]"));
        Assert.NotEmpty(cut.FindAll($"[data-testid=guardian-make-primary-{SecondId}]"));
    }

    [Fact]
    public async Task Set_primary_and_change_relation_send_the_update()
    {
        var cut = RenderTab();
        await cut.Find($"[data-testid=guardian-make-primary-{SecondId}]").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => Assert.Single(GuardiansApi.LinkUpdates));
        Assert.Equal((SecondId, GuardianRelations.Father, true),
            (GuardiansApi.LinkUpdates[0].GuardianId, GuardiansApi.LinkUpdates[0].Request.Relation, GuardiansApi.LinkUpdates[0].Request.IsPrimary));
        Assert.Equal(1, _changed);

        var relation = StringSelect(cut, $"guardian-relation-{SecondId}");
        await cut.InvokeAsync(() => relation.Instance.ValueChanged.InvokeAsync(GuardianRelations.Uncle));
        await cut.WaitForAssertionAsync(() => Assert.Equal(2, GuardiansApi.LinkUpdates.Count));
        Assert.Equal(GuardianRelations.Uncle, GuardiansApi.LinkUpdates[1].Request.Relation);
        Assert.False(GuardiansApi.LinkUpdates[1].Request.IsPrimary);
    }

    [Fact]
    public async Task Unlink_last_required_guardian_shows_error()
    {
        Culture.Current = new CultureInfo("en");
        var cut = RenderTab();
        GuardiansApi.Failure = new ApiProblemException(409, "guardian_required", null, null);

        _ = cut.Find($"[data-testid=guardian-unlink-{PrimaryId}]").ClickAsync(new());
        await Dialogs.WaitForElement("[data-testid=confirm-yes]").ClickAsync(new());

        await _snackbars.WaitForAssertionAsync(() =>
            Assert.Contains("At least one guardian with complete data is required.", _snackbars.Markup));
        Assert.Empty(GuardiansApi.Unlinks);
        Assert.Equal(0, _changed);
    }

    [Fact]
    public async Task Unlink_confirmed_removes_the_link()
    {
        var cut = RenderTab();
        _ = cut.Find($"[data-testid=guardian-unlink-{SecondId}]").ClickAsync(new());
        await Dialogs.WaitForElement("[data-testid=confirm-yes]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Equal([(Students.All[0].Summary.Id, SecondId)], GuardiansApi.Unlinks));
        Assert.Equal(1, _changed);
    }

    [Fact]
    public async Task Link_existing_from_match_sends_ExistingGuardianId()
    {
        var cut = RenderTab();
        _ = cut.Find("[data-testid=guardian-link-new]").ClickAsync(new());
        (await Dialogs.WaitForElementAsync("input[data-testid=guardian-mobile-0]")).Input("0509999111");

        await (await Dialogs.WaitForElementAsync("[data-testid=guardian-link-0]", TimeSpan.FromSeconds(5))).ClickAsync(new());
        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(GuardiansApi.Links));
        Assert.Equal(FakeGuardiansApi.MonaId, GuardiansApi.Links[0].Input.ExistingGuardianId);
        Assert.Null(GuardiansApi.Links[0].Input.NewGuardian);
    }

    [Fact]
    public async Task Match_alert_shows_only_name_and_masked_mobile_and_dialog_is_titled()
    {
        Culture.Current = new CultureInfo("en");
        var cut = RenderTab();
        _ = cut.Find("[data-testid=guardian-link-new]").ClickAsync(new());
        Assert.Contains("Link guardian", (await Dialogs.WaitForElementAsync(".mud-dialog-title")).TextContent);
        (await Dialogs.WaitForElementAsync("input[data-testid=guardian-national-id-0]")).Input("29001010101");

        var alert = (await Dialogs.WaitForElementAsync("[data-testid=guardian-match-0]", TimeSpan.FromSeconds(5))).TextContent;
        Assert.Contains("Mona", alert);
        Assert.Contains("+*********111", alert);
        // D30: the registered guardian's national id and full mobile are never revealed before linking.
        Assert.DoesNotContain("29001010101", alert);
        Assert.DoesNotContain("0501111111", alert);
    }

    [Fact]
    public async Task Link_new_guardian_requires_names_and_mobile()
    {
        var cut = RenderTab();
        _ = cut.Find("[data-testid=guardian-link-new]").ClickAsync(new());
        (await Dialogs.WaitForElementAsync("input[data-testid=guardian-name-ar-0]")).Change("سامي");
        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=guardian-error-0]");
        Assert.Empty(GuardiansApi.Links);

        Dialogs.Find("input[data-testid=guardian-name-en-0]").Change("Sami");
        Dialogs.Find("input[data-testid=guardian-mobile-0]").Input("0503333333");
        await Task.Delay(800); // the mobile field is debounced (500 ms) because typing it looks for a registered guardian
        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(GuardiansApi.Links));
        Assert.Equal("Sami", GuardiansApi.Links[0].Input.NewGuardian!.NameEn);
    }

    [Fact]
    public async Task Invite_disabled_without_email_or_when_the_portal_is_off()
    {
        Culture.Current = new CultureInfo("en");
        var student = StudentWithGuardians with { Guardians = [StudentWithGuardians.Guardians[1] with { Email = null }, StudentWithGuardians.Guardians[1] with { GuardianId = Guid.NewGuid(), Email = "k@x.test" }] };
        var emailed = student.Guardians[1].GuardianId;
        var noEmail = student.Guardians[0].GuardianId;

        // Portal off (default): both disabled and the alert explains why.
        var off = RenderTab(student: student);
        off.WaitForElement("[data-testid=portal-disabled]");
        Assert.True(off.Find($"[data-testid=guardian-invite-{emailed}]").HasAttribute("disabled"));

        // Portal on: only the one without an email stays disabled.
        Settings.Values[FeatureFlags.GuardianPortal.Name] = "true";
        var on = Render<StudentGuardiansTab>(p => p.Add(t => t.Student, student).Add(t => t.CanEdit, true));
        await on.WaitForAssertionAsync(() => Assert.Empty(on.FindAll("[data-testid=portal-disabled]")));
        Assert.True(on.Find($"[data-testid=guardian-invite-{noEmail}]").HasAttribute("disabled"));
        Assert.False(on.Find($"[data-testid=guardian-invite-{emailed}]").HasAttribute("disabled"));

        await on.Find($"[data-testid=guardian-invite-{emailed}]").ClickAsync(new());
        await on.WaitForAssertionAsync(() => Assert.Equal([emailed], GuardiansApi.Invitations));
    }

    [Fact]
    public void View_only_has_no_actions()
    {
        var cut = RenderTab(canEdit: false);

        cut.WaitForElement("[data-testid=guardians-table]");
        Assert.Empty(cut.FindAll("[data-testid=guardian-link-new]"));
        Assert.Empty(cut.FindAll($"[data-testid=guardian-unlink-{PrimaryId}]"));
        Assert.Empty(cut.FindAll($"[data-testid=guardian-invite-{SecondId}]"));
    }

    [Fact]
    public void A_role_without_Settings_View_still_gets_the_guardians_tab()
    {
        Settings.Failure = new Lms.Client.Services.ApiForbiddenException();
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        var before = navigation.Uri;

        var cut = RenderTab();

        cut.WaitForElement("[data-testid=guardians-table]");
        Assert.Equal(before, navigation.Uri); // not sent to /forbidden
    }
}
