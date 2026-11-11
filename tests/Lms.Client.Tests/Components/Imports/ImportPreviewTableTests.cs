using System.Globalization;
using Bunit;
using Lms.Client.Components.Imports;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Imports;
using MudBlazor;

namespace Lms.Client.Tests.Components.Imports;

public sealed class ImportPreviewTableTests : ClientTestContext
{
    private readonly List<(int Page, bool ErrorsOnly)> _requests = [];

    private static readonly ImportRowDto[] Rows =
    [
        new(2, new Dictionary<string, string?> { ["email"] = "a@x.com", ["nameEn"] = "Ann" }, []),
        new(3, new Dictionary<string, string?> { ["email"] = "bad", ["nameEn"] = "Bob" },
            [new ImportRowErrorDto("email", "invalid_email", "Not an email address")]),
    ];

    private IRenderedComponent<ImportPreviewTable> RenderTable()
    {
        Render<MudPopoverProvider>();
        return Render<ImportPreviewTable>(p => p
            .Add(t => t.Columns, StubImportsApi.Columns)
            .Add(t => t.LoadRows, (page, size, errorsOnly, _) =>
            {
                _requests.Add((page, errorsOnly));
                var rows = errorsOnly ? Rows.Where(r => r.Errors.Count > 0).ToList() : Rows.ToList();
                return Task.FromResult(new PagedResult<ImportRowDto>(rows, rows.Count, page, size));
            }));
    }

    [Fact]
    public void First_page_is_requested_with_all_rows()
    {
        var cut = RenderTable();

        cut.WaitForElement("[data-testid=import-row-2]");
        cut.WaitForElement("[data-testid=import-row-3]");
        Assert.Equal((1, false), _requests[0]);
    }

    [Fact]
    public async Task Errors_only_toggle_requests_errorsOnly_rows()
    {
        var cut = RenderTable();
        cut.WaitForElement("[data-testid=import-row-2]");

        await cut.Find("[data-testid=import-errors-only]").ChangeAsync(new() { Value = true });

        cut.WaitForAssertion(() => Assert.Contains((1, true), _requests));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=import-row-2]")));
        Assert.NotEmpty(cut.FindAll("[data-testid=import-row-3]"));
    }

    [Fact]
    public void Errored_cell_is_marked_and_has_message()
    {
        Culture.Current = new CultureInfo("en");
        var cut = RenderTable();

        var cell = cut.WaitForElement("[data-testid=import-cell-3-email]");

        Assert.Equal("Not an email address", cell.GetAttribute("title"));
        Assert.Contains("lms-import-error", cell.ClassList);
        Assert.Equal("invalid_email", cell.GetAttribute("data-error"));
        Assert.Empty(cut.FindAll("[data-testid=import-cell-2-email]")); // a valid cell carries no marker
    }
}
