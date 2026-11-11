using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Lms.Shared.Auditing;

namespace Lms.Api.Features.Audit;

/// <summary>Writes audit rows as UTF-8 CSV (with BOM, so Excel shows Arabic correctly) or as an xlsx workbook.</summary>
public static class AuditExporter
{
    public static readonly string[] Headers =
    [
        "Timestamp (UTC)", "User email", "User (ar)", "User (en)", "Role (ar)", "Role (en)", "Unit (ar)", "Unit (en)",
        "Action", "Entity", "Entity id", "IP address",
    ];

    public static async Task WriteCsvAsync(IEnumerable<AuditEntryDto> rows, Stream output, CancellationToken ct)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true)
        {
            NewLine = "\r\n",
        };
        await writer.WriteLineAsync(string.Join(',', Headers.Select(Quote)));
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(',', Cells(row).Select(Quote)));
        }
    }

    public static void WriteXlsx(IEnumerable<AuditEntryDto> rows, Stream output)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Audit");
        for (var c = 0; c < Headers.Length; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = Headers[c];
            cell.Style.Font.Bold = true;
        }

        var r = 2;
        foreach (var row in rows)
        {
            var cells = Cells(row);
            for (var c = 0; c < cells.Length; c++)
            {
                // Always text: a value must never be interpreted as a formula by Excel.
                sheet.Cell(r, c + 1).SetValue(Neutralize(cells[c]));
            }

            r++;
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, 100);
        workbook.SaveAs(output);
    }

    private static string[] Cells(AuditEntryDto r) =>
    [
        r.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), r.UserEmail ?? string.Empty,
        r.UserDisplayNameAr ?? string.Empty, r.UserDisplayNameEn ?? string.Empty, r.RoleNameAr ?? string.Empty,
        r.RoleNameEn ?? string.Empty, r.OrgUnitNameAr ?? string.Empty, r.OrgUnitNameEn ?? string.Empty,
        r.Action, r.EntityType, r.EntityId.ToString(), r.IpAddress ?? string.Empty,
    ];

    /// <summary>Prefixes cells that start like a formula (CSV/Excel injection).</summary>
    private static string Neutralize(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;

    private static string Quote(string value) => "\"" + Neutralize(value).Replace("\"", "\"\"") + "\"";
}
