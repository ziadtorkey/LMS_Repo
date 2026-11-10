using System.Globalization;
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

    [Fact]
    public void Arabic_pager_is_localized()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("ar");
        var rows = Enumerable.Range(1, 60).Select(i => $"row{i}").ToArray();
        var cut = RenderTable(rows);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("عدد الصفوف في الصفحة", cut.Markup);
            Assert.Contains("من 60", cut.Markup);
            Assert.DoesNotContain("Rows per page", cut.Markup);
        });
    }

    [Fact]
    public async Task Failing_loader_shows_a_message_with_retry_instead_of_throwing()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        var calls = 0;
        Render<MudPopoverProvider>();
        var cut = Render<ServerTable<string>>(p => p
            .Add(t => t.ServerData, (state, ct) =>
            {
                calls++;
                if (calls == 1) throw new Lms.Client.Services.Api.ApiProblemException(500, null, null, null);
                return Task.FromResult(new TableData<string> { Items = ["ok-row"], TotalItems = 1 });
            })
            .Add(t => t.RowTemplate, (RenderFragment<string>)(row => b => b.AddMarkupContent(0, $"<td>{row}</td>"))));

        cut.WaitForAssertion(() => Assert.Contains("Something went wrong", cut.Find("[data-testid=table-error]").TextContent));

        await cut.Find("[data-testid=table-retry]").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Contains("ok-row", cut.Markup));
    }
}
