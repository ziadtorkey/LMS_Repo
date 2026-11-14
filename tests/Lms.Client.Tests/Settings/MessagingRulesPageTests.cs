using System.Globalization;
using Bunit;
using Lms.Client.Components.Messaging;
using Lms.Client.Pages.Settings;
using Lms.Client.Services.Api;
using Lms.Client.Services.Messaging;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Settings;

/// <summary>The messaging rules page (M6 Task 27, FR-MSG-02, D69): the platform default is the ceiling, the floor is locked, only changed cells are saved.</summary>
public sealed class MessagingRulesPageTests : ClientTestContext
{
    private readonly FakeMessagingRules _rules = new();
    private readonly FakeCommsApi _comms;

    public MessagingRulesPageTests()
    {
        _comms = new FakeCommsApi(_rules);
        Services.AddSingleton<ICommsApi>(_comms);
    }

    private async Task<IRenderedComponent<MessagingRulesPage>> OpenAsync(string culture = "en", params string[] permissions)
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        await GrantAsync(permissions.Length == 0 ? [Permissions.Comms.Manage] : permissions);
        Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
        var page = Render<MessagingRulesPage>();
        if (permissions.Length == 0) page.WaitForElement("[data-testid=rules-matrix]");
        return page;
    }

    private static RuleCellValue Cell(IRenderedComponent<MessagingRulesPage> page, MessagingRole from, MessagingRole to) =>
        page.FindComponent<MessagingRulesMatrix>().Instance.Cells[(from, to)];

    [Fact]
    public async Task Page_needs_comms_manage_at_the_root()
    {
        var page = await OpenAsync("en", Permissions.Courses.View);

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=rules-title]")));
    }

    [Fact]
    public async Task Floor_cells_are_locked_chips_with_the_safeguarding_text()
    {
        var page = await OpenAsync();

        var studentToStudent = page.Find("[data-testid=rules-Student-Student]");
        Assert.NotEmpty(studentToStudent.QuerySelectorAll(".lms-rule-chip.locked"));
        Assert.Contains("Set by the platform", studentToStudent.TextContent);
        Assert.Empty(studentToStudent.QuerySelectorAll(".mud-select")); // no dropdown on a locked cell
        Assert.Contains("Groups only", page.Find("[data-testid=rules-Manager-Student]").TextContent);
    }

    [Fact]
    public async Task Cells_offer_only_values_at_or_below_the_default()
    {
        var page = await OpenAsync();
        var matrix = page.FindComponent<MessagingRulesMatrix>().Instance;
        var studentToTeacher = Cell(page, MessagingRole.Student, MessagingRole.Teacher); // default: with a relationship

        Assert.False(await page.InvokeAsync(() => matrix.SetAsync(studentToTeacher, MessagingVia.Anywhere))); // wider: refused
        Assert.Empty(page.FindAll("[data-testid=rules-bar]"));
        Assert.True(await page.InvokeAsync(() => matrix.SetAsync(studentToTeacher, MessagingVia.NotAllowed))); // narrower: taken
        page.WaitForElement("[data-testid=rules-bar]");
    }

    [Fact]
    public async Task Saving_sends_exactly_the_changed_rules()
    {
        var page = await OpenAsync();
        var matrix = page.FindComponent<MessagingRulesMatrix>().Instance;
        await page.InvokeAsync(() => matrix.SetAsync(Cell(page, MessagingRole.Teacher, MessagingRole.Student), MessagingVia.NotAllowed));
        await page.InvokeAsync(() => matrix.SetAsync(Cell(page, MessagingRole.Teacher, MessagingRole.Counselor), MessagingVia.SameUnit));

        await page.Find("[data-testid=rules-save]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=rules-bar]")));
        var sent = _comms.LastRules!.Cells;
        Assert.Equal(2, sent.Count);
        Assert.Contains(sent, c => (c.FromRole, c.ToRole, c.Mode) == (BuiltInRoles.Teacher, BuiltInRoles.Student, MessagingRuleModes.Deny));
        Assert.Contains(sent, c => (c.FromRole, c.ToRole, c.Mode) == (BuiltInRoles.Teacher, BuiltInRoles.Counselor, MessagingRuleModes.SameUnit));
    }

    [Fact]
    public async Task Rules_looser_than_default_maps_to_the_cell()
    {
        var page = await OpenAsync();
        var matrix = page.FindComponent<MessagingRulesMatrix>().Instance;
        await page.InvokeAsync(() => matrix.SetAsync(Cell(page, MessagingRole.Teacher, MessagingRole.Student), MessagingVia.NotAllowed));
        _comms.RulesFailure = new ApiProblemException(400, ProblemCodes.rules_looser_than_default, null, null, "Student>Teacher");

        await page.Find("[data-testid=rules-save]").ClickAsync(new());

        Assert.Contains("only narrow", page.WaitForElement("[data-testid=rules-error]").TextContent);
        Assert.NotNull(page.Find("[data-testid=rules-refused-Student-Teacher]")); // the cell the server named
        Assert.NotEmpty(page.FindAll("[data-testid=rules-bar]")); // nothing is lost
    }

    [Fact]
    public async Task Matrix_cells_are_labelled_selects()
    {
        var page = await OpenAsync();

        Assert.Contains("aria-label=\"Teacher → Counselor\"", page.Markup);
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        var page = await OpenAsync("ar");

        Assert.Equal("قواعد المراسلة", page.Find("[data-testid=rules-title]").TextContent.Trim());
    }
}
