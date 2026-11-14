using System.Globalization;
using Bunit;
using Lms.Client.Layout;
using Lms.Client.Pages.Organization;
using Lms.Client.Resources;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Organizations;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Tests.Pages.Organization;

public sealed class SettingsTabTests : ClientTestContext
{
    private readonly Guid _unit = Guid.NewGuid();
    private const string Upload = "Files.MaxUploadMb";
    private const string Flag = "Features.VirtualClasses";

    private IRenderedComponent<SettingsTab> RenderTab(bool canEdit = true)
    {
        Culture.Current = new CultureInfo("en");
        Render<MudBlazor.MudPopoverProvider>();
        var cut = Render<SettingsTab>(p => p.Add(t => t.UnitId, _unit).Add(t => t.CanEdit, canEdit));
        cut.WaitForElement($"[data-testid='setting-row-{Upload}']");
        return cut;
    }

    private void SetEffective(string name, Func<EffectiveSettingDto, EffectiveSettingDto> change)
    {
        var i = Config.Settings.FindIndex(s => s.Name == name);
        Config.Settings[i] = change(Config.Settings[i]);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Every_catalog_setting_and_policy_has_ar_and_en_label(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        var l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        var missing = SettingCatalog.All.Select(d => "Setting_" + d.Name.Replace('.', '_'))
            .Concat(PolicyCatalog.BlockedPermissions.Keys.Select(k => "Policy_" + k))
            .Concat(SettingCatalog.All.Select(d => "SettingCategory_" + d.Category).Distinct())
            .Where(key => l[key].ResourceNotFound).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Locked_by_ancestor_setting_is_read_only()
    {
        SetEffective(Upload, s => s with { ValueJson = "500", IsLocked = true, LockedByAncestor = true, SourceOrgUnitId = Guid.NewGuid(), SourceNameEn = "Main Group", SourceNameAr = "المجموعة" });

        var cut = RenderTab();

        Assert.True(cut.Find($"input[data-testid='setting-input-{Upload}']").HasAttribute("disabled"));
        Assert.True(cut.Find($"input[data-testid='setting-lock-{Upload}']").HasAttribute("disabled"));
        Assert.Contains("Locked by Main Group", cut.Find($"[data-testid='setting-source-{Upload}']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='setting-reset-{Upload}']"));
    }

    [Fact]
    public void Int_setting_out_of_range_shows_validation_error()
    {
        var cut = RenderTab();

        cut.Find($"input[data-testid='setting-input-{Upload}']").Change("5000");

        cut.WaitForAssertion(() => Assert.Contains("between 1 and 2048", cut.Find($"[data-testid='setting-error-{Upload}']").TextContent));
        Assert.Empty(Config.SettingCalls);
    }

    [Fact]
    public void Valid_int_value_is_saved_as_json()
    {
        var cut = RenderTab();

        cut.Find($"input[data-testid='setting-input-{Upload}']").Change("300");

        cut.WaitForAssertion(() => Assert.Equal("300", Assert.Single(Config.SettingCalls).Request.ValueJson));
        Assert.Equal(_unit, Config.SettingCalls[0].Unit);
        Assert.False(Config.SettingCalls[0].Request.IsLocked);
    }

    [Fact]
    public void Feature_flags_render_as_switches_in_features_group()
    {
        var cut = RenderTab();

        var group = cut.Find("[data-testid=settings-group-Features]");
        Assert.Equal(5, group.QuerySelectorAll("input[type=checkbox][data-testid^=setting-input-Features]").Length);
        Assert.Empty(cut.Find("[data-testid=settings-group-Files]").QuerySelectorAll("input[data-testid^=setting-input-Features]"));
    }

    [Fact]
    public void Toggling_a_feature_flag_saves_true()
    {
        var cut = RenderTab();

        cut.Find($"input[data-testid='setting-input-{Flag}']").Change(true);

        cut.WaitForAssertion(() => Assert.Equal("true", Assert.Single(Config.SettingCalls).Request.ValueJson));
    }

    [Fact]
    public void StringSet_setting_renders_one_checkbox_per_allowed_value_and_saves_a_json_array()
    {
        const string critical = "Students.CriticalFields";
        var cut = RenderTab();

        var allowed = SettingCatalog.Find(critical)!.AllowedValues!;
        Assert.Equal(allowed.Count, cut.FindAll($"input[type=checkbox][data-testid^='setting-input-{critical}-']").Count);
        Assert.Contains("Arabic name", cut.Find($"[data-testid='setting-row-{critical}']").TextContent);

        cut.Find($"input[data-testid='setting-input-{critical}-Mobile']").Change(true);

        cut.WaitForAssertion(() => Assert.Equal(
            "[\"NameAr\",\"NameEn\",\"DateOfBirth\",\"Gender\",\"Religion\",\"Nationality\",\"NationalId\",\"Mobile\"]",
            Assert.Single(Config.SettingCalls).Request.ValueJson));
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Every_enum_and_string_set_value_of_an_M3_setting_has_a_label(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        var l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();
        string[] m3 = ["Students.CodeMode", "Students.CriticalFields", "Classes.CapacityMode", "Blueprints.SectionLabel"];

        var missing = m3.SelectMany(n => SettingCatalog.Find(n)!.AllowedValues!
                .Select(v => $"SettingValue_{n.Replace('.', '_')}_{v}"))
            .Where(key => l[key].ResourceNotFound).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Lock_checkbox_saves_the_lock_with_the_current_value()
    {
        var cut = RenderTab();

        cut.Find($"input[data-testid='setting-lock-{Upload}']").Change(true);

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(Config.SettingCalls);
            Assert.Equal("200", call.Request.ValueJson);
            Assert.True(call.Request.IsLocked);
        });
    }

    [Fact]
    public void Source_line_distinguishes_default_set_here_and_inherited()
    {
        SetEffective(Flag, s => s with { ValueJson = "true", DefinedHere = true, SourceOrgUnitId = _unit, SourceNameEn = "This" });
        SetEffective("Features.Messaging", s => s with { ValueJson = "true", SourceOrgUnitId = Guid.NewGuid(), SourceNameEn = "Main Group", SourceNameAr = "المجموعة" });

        var cut = RenderTab();

        Assert.Contains("Default", cut.Find($"[data-testid='setting-source-{Upload}']").TextContent);
        Assert.Contains("Set here", cut.Find($"[data-testid='setting-source-{Flag}']").TextContent);
        Assert.Contains("Inherited from Main Group", cut.Find("[data-testid='setting-source-Features.Messaging']").TextContent);
    }

    [Fact]
    public void Reset_is_offered_only_where_defined_here_and_removes_the_value()
    {
        SetEffective(Flag, s => s with { ValueJson = "true", DefinedHere = true, SourceOrgUnitId = _unit, SourceNameEn = "This" });
        var cut = RenderTab();

        Assert.Empty(cut.FindAll($"[data-testid='setting-reset-{Upload}']"));
        cut.Find($"[data-testid='setting-reset-{Flag}']").Click();

        cut.WaitForAssertion(() => Assert.Equal((_unit, Flag), Assert.Single(Config.Removed)));
    }

    [Fact]
    public void Without_update_permission_everything_is_read_only()
    {
        var cut = RenderTab(canEdit: false);

        Assert.True(cut.Find($"input[data-testid='setting-input-{Flag}']").HasAttribute("disabled"));
        Assert.True(cut.Find($"input[data-testid='setting-lock-{Upload}']").HasAttribute("disabled"));
    }

    [Fact]
    public void Stale_context_while_saving_goes_to_the_chooser()
    {
        Config.SettingFailure = new Lms.Client.Services.Api.ApiContextException("invalid_context");
        var cut = RenderTab();

        cut.Find($"input[data-testid='setting-input-{Flag}']").Change(true);

        cut.WaitForAssertion(() => Assert.EndsWith("/choose-context", Services.GetRequiredService<NavigationManager>().Uri));
    }

    [Fact]
    public async Task Tabs_render_rtl_in_arabic()
    {
        var unit = new OrgUnitNodeDto(_unit, null, Guid.NewGuid(), "ع-وحدة", "Unit", "U", 0, "Active", false, null, false, false, []);
        Org.Units.Add(unit);
        await GrantAsync(Permissions.OrgUnits.View, Permissions.Settings.View, Permissions.Policies.View);
        Culture.Current = new CultureInfo("ar");

        var layout = Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b =>
        {
            b.OpenComponent<UnitSettingsPage>(0);
            b.AddComponentParameter(1, "Id", _unit);
            b.CloseComponent();
        })));

        layout.WaitForElement("[data-testid=unit-tabs]");
        Assert.Equal("rtl", layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        var tabs = layout.Find("[data-testid=unit-tabs]").TextContent;
        Assert.Contains("الإعدادات", tabs);
        Assert.Contains("السياسات", tabs);
    }

    [Fact]
    public void Each_setting_category_is_a_card_with_an_h2_heading()
    {
        var cut = RenderTab();

        var groups = cut.FindAll("[data-testid^=settings-group-]");
        Assert.NotEmpty(groups);
        Assert.All(groups, g =>
        {
            Assert.Contains("lms-section", g.ClassName);
            Assert.NotNull(g.QuerySelector("h2"));
            Assert.Null(g.QuerySelector("h3"));
        });
    }
}
