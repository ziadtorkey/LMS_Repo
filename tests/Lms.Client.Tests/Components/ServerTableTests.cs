using Bunit;
using Lms.Client.Components;
using Lms.Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Lms.Client.Tests.Components;

public sealed class ServerTableTests : ClientTestContext
{
    private IRenderedComponent<ServerTable<string>> RenderTable(string[] rows)
    {
        Render<MudPopoverProvider>();
        return Render<ServerTable<string>>(p => p
            .Add(t => t.ServerData, (state, ct) => Task.FromResult(new TableData<string> { Items = rows, TotalItems = rows.Length }))
            .Add(t => t.RowTemplate, (RenderFragment<string>)(row => b => b.AddMarkupContent(0, $"<td>{row}</td>"))));
    }

    [Fact]
    public void Renders_server_rows()
    {
        var cut = RenderTable(["alpha", "beta"]);
        cut.WaitForAssertion(() => Assert.Contains("beta", cut.Markup));
    }

    [Fact]
    public void Shows_localized_empty_state_without_rows()
    {
        var cut = RenderTable([]);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=table-empty]")));
    }
}
