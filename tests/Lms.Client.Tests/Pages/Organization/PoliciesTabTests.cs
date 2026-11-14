using System.Globalization;
using Bunit;
using Lms.Client.Pages.Organization;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Pages.Organization;

public sealed class PoliciesTabTests : ClientTestContext
{
    private readonly Guid _unit = Guid.NewGuid();
    private const string Key = PolicyCatalog.CreateOrgUnits;

    private IRenderedComponent<PoliciesTab> RenderTab(bool canEdit = true)
    {
        Culture.Current = new CultureInfo("en");
        return Render<PoliciesTab>(p => p.Add(t => t.UnitId, _unit).Add(t => t.CanEdit, canEdit));
    }

    [Fact]
    public void Inherited_policy_switch_is_disabled_and_names_source()
    {
        Config.Policies[0] = new EffectivePolicyDto(Key, PolicyStates.Inherited, Guid.NewGuid(), "المجموعة", "Main Group");

        var cut = RenderTab();
        cut.WaitForElement($"input[data-testid=policy-switch-{Key}]");

        Assert.True(cut.Find($"input[data-testid=policy-switch-{Key}]").HasAttribute("disabled"));
        Assert.Contains("Set by Main Group", cut.Find($"[data-testid=policy-source-{Key}]").TextContent);
    }

    [Fact]
    public async Task Toggling_policy_calls_enable_then_disable()
    {
        var cut = RenderTab();
        cut.WaitForElement($"input[data-testid=policy-switch-{Key}]");

        cut.Find($"input[data-testid=policy-switch-{Key}]").Change(true);
        cut.WaitForAssertion(() => Assert.Equal((_unit, Key), Assert.Single(Config.Enabled)));

        cut.WaitForAssertion(() => Assert.True(cut.Find($"input[data-testid=policy-switch-{Key}]").HasAttribute("checked")));
        cut.Find($"input[data-testid=policy-switch-{Key}]").Change(false);
        cut.WaitForAssertion(() => Assert.Equal((_unit, Key), Assert.Single(Config.Disabled)));
        await Task.CompletedTask;
    }

    [Fact]
    public void Blocked_actions_are_listed_with_their_labels()
    {
        var cut = RenderTab();
        cut.WaitForElement($"[data-testid=policy-blocks-{Key}]");

        Assert.Contains("Blocks:", cut.Find($"[data-testid=policy-blocks-{Key}]").TextContent);
        Assert.Contains("Create units", cut.Find($"[data-testid=policy-blocks-{Key}]").TextContent);
    }

    [Fact]
    public void Without_update_permission_the_switch_is_read_only()
    {
        var cut = RenderTab(canEdit: false);
        cut.WaitForElement($"input[data-testid=policy-switch-{Key}]");
        Assert.True(cut.Find($"input[data-testid=policy-switch-{Key}]").HasAttribute("disabled"));
    }

    [Fact]
    public void Stale_context_while_toggling_goes_to_the_chooser()
    {
        Config.PolicyFailure = new ApiContextException("invalid_context");
        var cut = RenderTab();
        cut.WaitForElement($"input[data-testid=policy-switch-{Key}]");

        cut.Find($"input[data-testid=policy-switch-{Key}]").Change(true);

        cut.WaitForAssertion(() => Assert.EndsWith("/choose-context", Services.GetRequiredService<NavigationManager>().Uri));
        Assert.Empty(cut.FindAll("[data-testid=policies-error]"));
    }

    [Fact]
    public void Inherited_disable_conflict_shows_the_policy_inherited_message()
    {
        Config.PolicyFailure = new ApiProblemException(409, "policy_inherited", null, null);
        var cut = RenderTab();
        cut.WaitForElement($"input[data-testid=policy-switch-{Key}]");

        cut.Find($"input[data-testid=policy-switch-{Key}]").Change(true);

        cut.WaitForAssertion(() => Assert.Contains("set by a parent unit", cut.Find("[data-testid=policies-error]").TextContent));
    }

    [Fact]
    public void Policies_explain_the_cascade_and_sit_as_toggle_rows_in_one_card()
    {
        var cut = RenderTab();
        cut.WaitForElement($"input[data-testid=policy-switch-{Key}]");

        Assert.Contains("mud-alert-outlined-info", cut.Find("[data-testid=policies-cascade-note]").ClassName);
        var card = cut.Find("[data-testid=policies-card]");
        Assert.NotNull(card.QuerySelector("h2"));
        var row = cut.Find($"[data-testid=policy-row-{Key}]");
        Assert.Contains("lms-toggle-row", row.ClassName);
        Assert.NotNull(row.QuerySelector(".lms-toggle-help"));
    }
}
