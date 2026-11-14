using ClosedXML.Excel;
using Lms.Domain.Grading;
using Lms.Infrastructure.Spreadsheets;
using Lms.Shared.Grading;

namespace Lms.Infrastructure.Grading.Exchange;

/// <summary>
/// The grade report as an <c>.xlsx</c> (M5 Task 10, L27): the "Report" sheet with the rows and a "Scale" sheet that carries the scale's name, kind and bands with
/// their Arabic and English descriptors, so a printed or forwarded report is never read without its legend. Every text cell goes through
/// <see cref="SpreadsheetCell.Safe"/>. The report endpoints themselves are Task 9's; this renders their <see cref="GradeReportDto"/>.
/// </summary>
public static class GradeReportExcel
{
    public static byte[] Render(GradeReportDto report, GradingScale? scale, IReadOnlyList<GradingScaleBand> bands)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Report");
        string[] headers = ["StudentNumber", "NameEn", "NameAr", "CourseEn", "CourseAr", "FinalPercent", "Band", "BandEn", "BandAr", "BandPoints"];
        WriteHeader(sheet, headers);
        var r = 2;
        foreach (var row in report.Rows.Items)
        {
            sheet.Cell(r, 1).SetValue(SpreadsheetCell.Safe(row.StudentNumber));
            sheet.Cell(r, 2).SetValue(SpreadsheetCell.Safe(row.StudentNameEn));
            sheet.Cell(r, 3).SetValue(SpreadsheetCell.Safe(row.StudentNameAr));
            sheet.Cell(r, 4).SetValue(SpreadsheetCell.Safe(row.CourseNameEn));
            sheet.Cell(r, 5).SetValue(SpreadsheetCell.Safe(row.CourseNameAr));
            if (row.FinalPercent is { } percent)
            {
                sheet.Cell(r, 6).SetValue(percent);
            }

            sheet.Cell(r, 7).SetValue(SpreadsheetCell.Safe(row.BandCode));
            sheet.Cell(r, 8).SetValue(SpreadsheetCell.Safe(row.BandLabelEn));
            sheet.Cell(r, 9).SetValue(SpreadsheetCell.Safe(row.BandLabelAr));
            if (row.BandPoints is { } points)
            {
                sheet.Cell(r, 10).SetValue(points);
            }

            r++;
        }

        sheet.SheetView.FreezeRows(1);
        var legend = workbook.Worksheets.Add("Scale");
        WriteHeader(legend, ["Scale", "Kind", "Code", "Min", "Max", "LabelEn", "LabelAr", "DescriptorEn", "DescriptorAr", "Points"]);
        var l = 2;
        foreach (var band in bands.OrderBy(b => b.Order))
        {
            legend.Cell(l, 1).SetValue(SpreadsheetCell.Safe(scale?.NameEn));
            legend.Cell(l, 2).SetValue(SpreadsheetCell.Safe(scale?.Kind));
            legend.Cell(l, 3).SetValue(SpreadsheetCell.Safe(band.Code));
            legend.Cell(l, 4).SetValue(band.MinPercent);
            legend.Cell(l, 5).SetValue(band.MaxPercent);
            legend.Cell(l, 6).SetValue(SpreadsheetCell.Safe(band.LabelEn));
            legend.Cell(l, 7).SetValue(SpreadsheetCell.Safe(band.LabelAr));
            legend.Cell(l, 8).SetValue(SpreadsheetCell.Safe(band.DescriptorEn));
            legend.Cell(l, 9).SetValue(SpreadsheetCell.Safe(band.DescriptorAr));
            if (band.Points is { } p)
            {
                legend.Cell(l, 10).SetValue(p);
            }

            l++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteHeader(IXLWorksheet sheet, string[] headers)
    {
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
            sheet.Cell(1, c + 1).Style.Font.Bold = true;
        }
    }
}
