using System.Globalization;
using Bunit;
using Lms.Client.Pages.Organization;
using Lms.Client.Tests.Support;
using Lms.Shared.Organizations;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Organization;

public sealed class CopyBranchDialogTests : ClientTestContext
{
    private readonly Guid _root = Guid.NewGuid(), _a = Guid.NewGuid(), _a1 = Guid.NewGuid(), _b = Guid.NewGuid();

    private OrgUnitNodeDto U(Guid id, Guid? parent, string en) =>
        new(id, parent, Guid.NewGuid(), "ع-" + en, en, en.ToUpperInvariant(), parent is null ? 0 : 1, "Active", false, null, false, false, []);

    private List<OrgUnitNodeDto> Units() => [U(_root, null, "Root"), U(_a, _root, "A"), U(_a1, _a, "A1"), U(_b, _root, "B")];

    private IRenderedComponent<MudDialogProvider> Open()
    {
        Culture.Current = new CultureInfo("en");
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var units = Units();
        _ = Services.GetRequiredService<IDialogService>().ShowAsync<CopyBranchDialog>("Copy", new DialogParameters<CopyBranchDialog>
        {
            { x => x.Source, units.First(u => u.Id == _a) }, { x => x.AllUnits, units },
        });
        dialogs.WaitForElement("[data-testid=dialog-save]");
        return dialogs;
    }

    [Fact]
    public void Targets_exclude_the_sources_own_branch_but_allow_its_current_parent()
    {
        var ids = CopyBranchDialog.Targets(Units().First(u => u.Id == _a), Units()).Select(u => u.Id).ToList();

        Assert.DoesNotContain(_a, ids);
        Assert.DoesNotContain(_a1, ids);
        Assert.Contains(_root, ids);
        Assert.Contains(_b, ids);
    }

    [Fact]
    public async Task Copy_dialog_requires_suffix_and_target()
    {
        var dialogs = Open();

        await dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());
        Assert.Empty(Org.Copies); // no target, no suffix

        var select = dialogs.FindComponent<MudSelect<Guid?>>();
        await dialogs.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync((Guid?)_b));
        await dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());
        Assert.Empty(Org.Copies); // still no suffix

        dialogs.Find("input[data-testid=copy-suffix]").Change("-2027");
        await dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        dialogs.WaitForAssertion(() => Assert.Single(Org.Copies));
        var sent = Org.Copies[0];
        Assert.Equal(_a, sent.SourceOrgUnitId);
        Assert.Equal(_b, sent.TargetParentId);
        Assert.Equal("-2027", sent.CodeSuffix);
        Assert.True(sent.IncludePolicies);
        Assert.True(sent.IncludeSettings);
    }

    [Fact]
    public async Task Copy_result_lists_skipped_settings()
    {
        Org.CopyResult = new CopyBranchResultDto(Guid.NewGuid(), 4, ["Files.MaxUploadMb", "Features.Messaging"]);
        var dialogs = Open();
        var select = dialogs.FindComponent<MudSelect<Guid?>>();
        await dialogs.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync((Guid?)_b));
        dialogs.Find("input[data-testid=copy-suffix]").Change("-2027");

        await dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        dialogs.WaitForAssertion(() => Assert.Contains("4 units created", dialogs.Find("[data-testid=copy-result]").TextContent));
        var skipped = dialogs.Find("[data-testid=copy-skipped]").TextContent;
        Assert.Contains("Files.MaxUploadMb", skipped);
        Assert.Contains("Features.Messaging", skipped);
    }
}
