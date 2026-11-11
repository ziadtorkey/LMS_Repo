using ClosedXML.Excel;

namespace Lms.Infrastructure.Imports;

/// <summary>The downloadable template: sheet "Data" with the headers, sheet "Instructions" describing every column.</summary>
public static class ImportTemplateWriter
{
    public static void Write(IImportDefinition definition, string culture, Stream output)
    {
        var arabic = culture.StartsWith("ar", StringComparison.OrdinalIgnoreCase);
        using var workbook = new XLWorkbook();

        var data = workbook.Worksheets.Add("Data");
        var columns = definition.Columns;
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var c = i + 1;
            data.Cell(1, c).Value = arabic ? column.HeaderAr : column.HeaderEn;
            if (column.AsText)
            {
                data.Column(c).Style.NumberFormat.Format = "@";
            }

            if (column.AllowedValues is { Count: > 0 } allowed)
            {
                var validation = data.Range(2, c, ImportReader_MaxRowsForValidation, c).CreateDataValidation();
                validation.List(string.Join(",", allowed), true);
            }

            data.Column(c).Width = Math.Max(14, (arabic ? column.HeaderAr : column.HeaderEn).Length + 4);
        }

        var header = data.Range(1, 1, 1, Math.Max(1, columns.Count));
        header.Style.Font.Bold = true;
        data.SheetView.FreezeRows(1);
        data.RightToLeft = arabic;

        var help = workbook.Worksheets.Add("Instructions");
        string[] titles = ["Column", "العمود", "Required", "مطلوب", "Allowed values", "القيم المسموحة", "Example", "مثال"];
        for (var i = 0; i < titles.Length; i++)
        {
            help.Cell(1, i + 1).Value = titles[i];
        }

        help.Range(1, 1, 1, titles.Length).Style.Font.Bold = true;
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var r = i + 2;
            help.Cell(r, 1).Value = column.HeaderEn;
            help.Cell(r, 2).Value = column.HeaderAr;
            help.Cell(r, 3).Value = column.Required ? "Yes" : "No";
            help.Cell(r, 4).Value = column.Required ? "نعم" : "لا";
            help.Cell(r, 5).Value = column.AllowedValues is null ? string.Empty : string.Join(", ", column.AllowedValues);
            help.Cell(r, 6).Value = column.AllowedValues is null ? string.Empty : string.Join(", ", column.AllowedValues);
            help.Cell(r, 7).Value = column.ExampleValue ?? string.Empty;
            help.Cell(r, 8).Value = column.ExampleValue ?? string.Empty;
        }

        help.Columns().AdjustToContents();
        workbook.SaveAs(output);
    }

    private const int ImportReader_MaxRowsForValidation = 5001;
}
