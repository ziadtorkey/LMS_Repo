using System.Globalization;
using Bunit;
using Lms.Client.Pages.Enrollment;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Classes;
using Lms.Shared.Enrollment;
using Lms.Shared.Organizations;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Enrollment;

public sealed class TransferDialogTests : StudentsTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly FakeEnrollmentApi _enrollment = new();
    private IRenderedComponent<StudentEnrollmentsTab> _tab = default!;

    public TransferDialogTests() => Services.AddSingleton<IEnrollmentApi>(_enrollment);

    /// <summary>Opens the dialog the way a user does: from the student's tab (the student is placed in Grade 1 A; Grade 1 B is the target).</summary>
    private async Task OpenAsync()
    {
        RenderProviders();
        _tab = Render<StudentEnrollmentsTab>(p => p.Add(t => t.Student, Students.All[0]).Add(t => t.CanTransfer, true));
        _ = _tab.WaitForElement("[data-testid=student-transfer]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=transfer-class]");
    }

    private async Task ChooseTargetAsync()
    {
        var select = Dialogs.FindComponents<MudSelect<Guid?>>().First(s => s.Instance.UserAttributes.TryGetValue("data-testid", out var v) && (string?)v == "transfer-class");
        await Dialogs.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(FakeClasses.Grade1B.Id));
        Dialogs.Find("textarea[data-testid=transfer-reason]").Input("Parent request");
    }

    [Fact]
    public async Task Targets_exclude_the_current_class()
    {
        Culture.Current = new CultureInfo("en");
        await OpenAsync();

        var select = Dialogs.FindComponents<MudSelect<Guid?>>().First(s => s.Instance.UserAttributes.TryGetValue("data-testid", out var v) && (string?)v == "transfer-class");
        var items = select.FindComponents<MudSelectItem<Guid?>>().Select(i => i.Instance.Value).ToList();
        Assert.Contains((Guid?)FakeClasses.Grade1B.Id, items);
        Assert.DoesNotContain((Guid?)FakeClasses.Grade1A.Id, items);
    }

    private static readonly Guid SchoolLevel = Guid.Parse("00000000-0000-0000-0000-0000000000b8");
    private static readonly Guid NurseryLevel = Guid.Parse("00000000-0000-0000-0000-0000000000b7");
    private static readonly Guid OtherSchool = Guid.Parse("00000000-0000-0000-0000-0000000000a8");
    private static readonly Guid UnrelatedUnit = Guid.Parse("00000000-0000-0000-0000-0000000000a7");

    private static ClassSummaryDto ClassIn(Guid unit, string unitEn, string code, string nameEn) =>
        FakeClasses.Grade1B with { Id = Guid.NewGuid(), Code = code, NameAr = nameEn, NameEn = nameEn, OrgUnitId = unit, OrgUnitNameAr = unitEn, OrgUnitNameEn = unitEn };

    private sealed class ClassesStub(IReadOnlyList<ClassSummaryDto> classes) : IClassesApi
    {
        public Task<PagedResult<ClassSummaryDto>> ListAsync(ClassQuery query, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<ClassSummaryDto>(classes.Where(c => query.OrgUnitId is null || c.OrgUnitId == query.OrgUnitId).ToList(), classes.Count, 1, 200));
    }

    private sealed class UnitsStub(params OrgUnitNodeDto[] units) : IUnitLookupApi
    {
        public Task<IReadOnlyList<OrgUnitNodeDto>> GetUnitsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<OrgUnitNodeDto>>(units);
    }

    private static OrgUnitNodeDto Unit(Guid id, string name, Guid levelType) =>
        new(id, FakeUnits.GroupId, levelType, name, name, name[..2], 2, "Active", false, null, false, false, []);

    [Fact]
    public async Task Targets_include_classes_of_other_units_with_the_same_level_type_only()
    {
        Culture.Current = new CultureInfo("en");
        var school = Unit(FakeUnits.SchoolId, "Main School", SchoolLevel);
        var sibling = ClassIn(OtherSchool, "North School", "2A", "Grade 2 A North");
        var unrelated = ClassIn(UnrelatedUnit, "Nursery", "3A", "Nursery A");
        Services.AddSingleton<IUnitLookupApi>(new UnitsStub(school, Unit(OtherSchool, "North School", SchoolLevel), Unit(UnrelatedUnit, "Nursery", NurseryLevel)));
        Services.AddSingleton<IClassesApi>(new ClassesStub([FakeClasses.Grade1A, FakeClasses.Grade1B, sibling, unrelated]));
        await OpenAsync();

        var select = Dialogs.FindComponents<MudSelect<Guid?>>().First(x => x.Instance.UserAttributes.TryGetValue("data-testid", out var v) && (string?)v == "transfer-class");
        var items = select.FindComponents<MudSelectItem<Guid?>>().ToList();
        var offered = items.Select(i => i.Instance.Value).ToList();

        Assert.Contains((Guid?)FakeClasses.Grade1B.Id, offered);   // same unit
        Assert.Contains((Guid?)sibling.Id, offered);               // another school of the same level type (D43)
        Assert.DoesNotContain((Guid?)unrelated.Id, offered);       // a different level type
        Assert.DoesNotContain((Guid?)FakeClasses.Grade1A.Id, offered); // the current class
    }

    [Fact]
    public async Task Transfer_invalid_shows_the_servers_reason_after_the_localized_message()
    {
        Culture.Current = new CultureInfo("en");
        await OpenAsync();
        await ChooseTargetAsync();
        _enrollment.Failure = new ApiProblemException(409, "transfer_invalid", "The target class belongs to a different academic year.", null);

        await Dialogs.Find("[data-testid=transfer-preview]").ClickAsync(new());

        var error = (await Dialogs.WaitForElementAsync("[data-testid=dialog-error]", Wait)).TextContent;
        Assert.Contains("The student cannot be transferred to this class.", error);
        Assert.Contains("The target class belongs to a different academic year.", error);
    }

    [Fact]
    public async Task Transfer_dialog_previews_before_confirm()
    {
        Culture.Current = new CultureInfo("en");
        await OpenAsync();
        Assert.True(Dialogs.Find("[data-testid=transfer-confirm]").HasAttribute("disabled")); // nothing to confirm before a preview
        await ChooseTargetAsync();

        await Dialogs.Find("[data-testid=transfer-preview]").ClickAsync(new());

        await Dialogs.WaitForElementAsync("[data-testid=transfer-preview-result]", Wait);
        Assert.Contains("MATH1", Dialogs.Find("[data-testid=preview-kept]").TextContent);
        Assert.Contains("ART1", Dialogs.Find("[data-testid=preview-dropped]").TextContent);
        Assert.Contains("ARAB1", Dialogs.Find("[data-testid=preview-added]").TextContent);
        Assert.Contains("FR1", Dialogs.Find("[data-testid=preview-restricted]").TextContent);
        Assert.Empty(_enrollment.Transfers); // previewing changes nothing
        Assert.False(Dialogs.Find("[data-testid=transfer-confirm]").HasAttribute("disabled"));

        await Dialogs.Find("[data-testid=transfer-confirm]").ClickAsync(new());

        await _tab.WaitForAssertionAsync(() => Assert.Single(_enrollment.Transfers), Wait);
        Assert.Equal((Students.All[0].Summary.Id, FakeClasses.Grade1B.Id, "Parent request"),
            (_enrollment.Transfers[0].StudentId, _enrollment.Transfers[0].ToClassId, _enrollment.Transfers[0].Reason));
    }

    [Fact]
    public async Task Changing_the_inputs_invalidates_the_preview()
    {
        await OpenAsync();
        await ChooseTargetAsync();
        await Dialogs.Find("[data-testid=transfer-preview]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=transfer-preview-result]", Wait);

        Dialogs.Find("textarea[data-testid=transfer-reason]").Input("Another reason");

        Assert.Empty(Dialogs.FindAll("[data-testid=transfer-preview-result]"));
        Assert.True(Dialogs.Find("[data-testid=transfer-confirm]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Transfer_invalid_reason_is_localized()
    {
        Culture.Current = new CultureInfo("en");
        await OpenAsync();
        await ChooseTargetAsync();
        _enrollment.Failure = new ApiProblemException(409, "transfer_invalid", null, null);

        await Dialogs.Find("[data-testid=transfer-preview]").ClickAsync(new());

        Assert.Contains("The student cannot be transferred to this class.", (await Dialogs.WaitForElementAsync("[data-testid=dialog-error]", Wait)).TextContent);
        Assert.Empty(_enrollment.Transfers);

    }

    [Fact]
    public async Task Over_capacity_requires_the_confirm_checkbox()
    {
        _enrollment.Preview = _enrollment.Preview with { OverCapacity = true };
        await OpenAsync();
        await ChooseTargetAsync();
        await Dialogs.Find("[data-testid=transfer-preview]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=transfer-over-capacity]", Wait);

        Assert.True(Dialogs.Find("[data-testid=transfer-confirm]").HasAttribute("disabled"));

        await Dialogs.Find("input[data-testid=transfer-over-capacity]").ChangeAsync(new() { Value = true });
        Assert.False(Dialogs.Find("[data-testid=transfer-confirm]").HasAttribute("disabled"));
        await Dialogs.Find("[data-testid=transfer-confirm]").ClickAsync(new());

        await _tab.WaitForAssertionAsync(() => Assert.Single(_enrollment.Transfers), Wait);
        Assert.True(_enrollment.Transfers[0].ConfirmOverCapacity);
    }

    [Fact]
    public async Task Dialog_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await OpenAsync();

        Assert.Contains("الفصل الجديد", Dialogs.Markup);
        Assert.Contains("معاينة الأثر", Dialogs.Find("[data-testid=transfer-preview]").TextContent);
    }
}
