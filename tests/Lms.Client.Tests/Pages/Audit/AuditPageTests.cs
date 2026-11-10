using System.Globalization;
using System.Xml.Linq;
using Bunit;
using Lms.Client.Pages.Audit;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Auditing;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Audit;

public sealed class AuditPageTests : ClientTestContext
{
    private readonly FakeAuditApi _audit = new();

    public AuditPageTests() => Services.AddSingleton<IAuditApi>(_audit);

    private async Task GrantAsync(params string[] permissions)
    {
        Api.Permissions = new(null, permissions);
        await Context.SetAsync(Guid.NewGuid());
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
    }

    private IRenderedComponent<AuditPage> RenderPage()
    {
        Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
        return Render<AuditPage>();
    }

    private static HashSet<string> Keys(string lang)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Lms.Client"))) dir = Path.GetDirectoryName(dir);
        var path = Path.Combine(dir!, "src", "Lms.Client", "Resources", $"SharedResource.{lang}.resx");
        return XDocument.Load(path).Descendants("data").Select(d => (string)d.Attribute("name")!).ToHashSet();
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Every_audit_catalog_entry_has_ar_and_en_label(string lang)
    {
        var keys = Keys(lang);
        var expected = AuditCatalog.EntityTypes.Select(t => $"AuditEntity_{t}")
            .Concat(AuditCatalog.SecurityEvents.Select(e => $"AuditEvent_{e}"))
            .Concat(AuditActions.All.Select(a => $"AuditAction_{a}"));

        Assert.All(expected, k => Assert.Contains(k, keys));
    }

    [Fact]
    public async Task Filters_are_sent_in_query()
    {
        await GrantAsync(Permissions.Audit.View);
        var cut = RenderPage();
        cut.WaitForAssertion(() => Assert.NotNull(_audit.LastQuery));
        Assert.Equal(1, _audit.LastQuery!.Page);
        Assert.Equal(50, _audit.LastQuery.PageSize);

        // Clicking a user name filters by that user.
        var row = (await _audit.QueryAsync(new AuditQuery(null, null, null, null, null, null, null))).Items
            .First(e => e.UserId == _audit.AdminId);
        await cut.WaitForElement($"[data-testid=audit-user-{row.Id}]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Equal(_audit.AdminId, _audit.LastQuery!.UserId));
        Assert.NotEmpty(cut.FindAll("[data-testid=filter-user]"));

        // The date filters become UTC bounds of whole days.
        var from = cut.FindComponents<MudDatePicker>()[0];
        await cut.InvokeAsync(() => from.Instance.DateChanged.InvokeAsync(new DateTime(2026, 9, 1)));
        cut.WaitForAssertion(() => Assert.Equal(AuditDateRange.StartUtc(new DateTime(2026, 9, 1), TimeZoneInfo.Local), _audit.LastQuery!.FromUtc));
    }

    [Fact]
    public async Task Export_buttons_hidden_without_Audit_Export()
    {
        await GrantAsync(Permissions.Audit.View);
        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.NotNull(_audit.LastQuery));
        Assert.Empty(cut.FindAll("[data-testid=audit-export-csv]"));
        Assert.Empty(cut.FindAll("[data-testid=audit-export-xlsx]"));
    }

    [Fact]
    public async Task Export_buttons_request_the_chosen_format_with_the_filters()
    {
        await GrantAsync(Permissions.Audit.View, Permissions.Audit.Export);
        var cut = RenderPage();

        await cut.Find("[data-testid=audit-export-xlsx]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Equal(AuditExportFormats.Xlsx, _audit.LastExportFormat));
    }

    [Fact]
    public async Task Detail_drawer_shows_the_diff_of_the_selected_entry()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        await GrantAsync(Permissions.Audit.View);
        var cut = RenderPage();
        var role = (await _audit.QueryAsync(new AuditQuery(null, null, "Role", null, null, null, null))).Items.Single();

        await cut.WaitForElement($"[data-testid=audit-open-{role.Id}]").ClickAsync(new());

        cut.WaitForElement("[data-testid=diff-NameEn]");
        Assert.Contains("Instructor", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid=diff-NameAr]")); // unchanged property is not listed
    }


    public static TheoryData<Exception> RedirectingFailures => new()
    {
        { new ApiContextException("invalid_context") },
        { new ApiForbiddenException() },
    };

    [Theory]
    [MemberData(nameof(RedirectingFailures))]
    public async Task Export_with_stale_context_or_forbidden_redirects_instead_of_crashing(Exception failure)
    {
        await GrantAsync(Permissions.Audit.View, Permissions.Audit.Export);
        var cut = RenderPage();
        _audit.NextExportFailure = failure;

        await cut.Find("[data-testid=audit-export-csv]").ClickAsync(new());

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith(failure is ApiContextException ? "/choose-context" : "/forbidden", navigation.Uri);
    }

    [Theory]
    [InlineData("export_too_large", "Too many rows to export")]
    [InlineData(null, "Could not load data")]
    public async Task Export_problem_or_offline_shows_a_snackbar(string? code, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        await GrantAsync(Permissions.Audit.View, Permissions.Audit.Export);
        Render<MudPopoverProvider>();
        var snackbars = Render<MudSnackbarProvider>();
        var cut = Render<AuditPage>();
        _audit.NextExportFailure = code is null ? new HttpRequestException("offline") : new ApiProblemException(409, code, null, null);

        await cut.Find("[data-testid=audit-export-csv]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Contains(expected, snackbars.Markup));
    }

    [Fact]
    public void Date_filter_uses_the_users_local_days_as_utc_bounds()
    {
        var riyadh = TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");
        var day = new DateTime(2026, 11, 11);

        Assert.Equal(new DateTime(2026, 11, 10, 21, 0, 0), AuditDateRange.StartUtc(day, riyadh));
        Assert.Equal(new DateTime(2026, 11, 11, 21, 0, 0).AddTicks(-1), AuditDateRange.EndUtc(day, riyadh));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic_with_arabic_values_intact()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("ar");
        await GrantAsync(Permissions.Audit.View);
        var detail = _audit.Add(_audit.AdminId, AuditActions.Updated, "Term", """{"NameAr":"الفصل الأول"}""", """{"NameAr":"الفصل الثاني"}""");
        Render<MudPopoverProvider>();
        var cut = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<AuditPage>());

        await cut.WaitForElement($"[data-testid=audit-open-{detail.Entry.Id}]").ClickAsync(new());

        var cell = cut.WaitForElement("[data-testid=diff-NameAr]");
        Assert.Contains("الفصل الثاني", cell.TextContent);
        Assert.Equal("auto", cell.QuerySelectorAll("td")[2].GetAttribute("dir"));
        Assert.Contains("سجل التدقيق", cut.Markup);
    }
}
