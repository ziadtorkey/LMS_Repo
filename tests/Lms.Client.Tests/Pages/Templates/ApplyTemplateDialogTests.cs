using System.Globalization;
using Bunit;
using Lms.Client.Pages.Templates;
using Lms.Client.Tests.Support;
using Lms.Shared.Organizations;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Templates;

public sealed class ApplyTemplateDialogTests : ClientTestContext
{
    private readonly Guid _targetId = Guid.NewGuid();
    private readonly OrgTemplateSummaryDto _template = new(Guid.NewGuid(), "قالب", "School template", TemplateVisibilities.Shared,
        Guid.NewGuid(), "م", "Owner", 2, ["Year", "Campus"], DateTime.UtcNow);

    private IRenderedComponent<MudDialogProvider> Open()
    {
        Culture.Current = new CultureInfo("en");
        var level = Guid.NewGuid();
        Templates.Items.Add(_template);
        Templates.Nodes[_template.Id] =
        [
            new(1, null, level, "مدرسة {Year}", "School {Year}", "S-{Year}", false, null, false, [], 0),
            new(2, 1, level, "فرع {Campus}", "Campus {Campus}", "C-{Campus}", false, null, false, [], 0),
        ];
        var units = new List<OrgUnitNodeDto>
        {
            new(_targetId, null, level, "الجذر", "Root", "R", 0, "Active", false, null, false, false, []),
        };
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        _ = Services.GetRequiredService<IDialogService>().ShowAsync<ApplyTemplateDialog>("Apply", new DialogParameters<ApplyTemplateDialog>
        {
            { x => x.Template, _template }, { x => x.AllUnits, units },
        });
        dialogs.WaitForElement("[data-testid=dialog-save]");
        return dialogs;
    }

    [Fact]
    public async Task Apply_dialog_requires_every_placeholder_and_previews_names()
    {
        var dialogs = Open();
        var select = dialogs.FindComponent<MudSelect<Guid?>>();
        await dialogs.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync((Guid?)_targetId));

        // one required input per placeholder
        Assert.NotEmpty(dialogs.FindAll("input[data-testid=placeholder-Year]"));
        Assert.NotEmpty(dialogs.FindAll("input[data-testid=placeholder-Campus]"));

        dialogs.Find("input[data-testid=placeholder-Year]").Input("2027");
        await dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());
        Assert.Empty(Templates.Applied); // Campus is still missing

        dialogs.Find("input[data-testid=placeholder-Campus]").Input("North");
        dialogs.WaitForAssertion(() =>
        {
            var preview = dialogs.Find("[data-testid=template-preview]").TextContent;
            Assert.Contains("School 2027", preview);
            Assert.Contains("Campus North", preview);
        });
        await dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        dialogs.WaitForAssertion(() => Assert.Single(Templates.Applied));
        var (id, request) = Templates.Applied[0];
        Assert.Equal(_template.Id, id);
        Assert.Equal(_targetId, request.TargetParentId);
        Assert.Equal("2027", request.Placeholders["Year"]);
        Assert.Equal("North", request.Placeholders["Campus"]);
    }

    [Fact]
    public async Task Apply_result_shows_units_created_and_skipped_settings()
    {
        Templates.ApplyResult = new ApplyTemplateResultDto(Guid.NewGuid(), 2, ["Files.MaxUploadMb"]);
        var dialogs = Open();
        var select = dialogs.FindComponent<MudSelect<Guid?>>();
        await dialogs.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync((Guid?)_targetId));
        dialogs.Find("input[data-testid=placeholder-Year]").Input("2027");
        dialogs.Find("input[data-testid=placeholder-Campus]").Input("North");

        await dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        dialogs.WaitForAssertion(() => Assert.Contains("2 units created", dialogs.Find("[data-testid=apply-result]").TextContent));
        Assert.Contains("Files.MaxUploadMb", dialogs.Find("[data-testid=apply-skipped]").TextContent);
    }
}
