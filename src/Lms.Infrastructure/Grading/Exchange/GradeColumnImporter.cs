using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using Lms.Shared.Grading;

namespace Lms.Infrastructure.Grading.Exchange;

/// <summary>One parsed data row of an uploaded column: the student number and the points (null = the cell is empty: no change).</summary>
public sealed record ParsedGradeRow(int Row, string StudentNumber, decimal? Points, string? ErrorCode);

public sealed record ParsedGradeFile(IReadOnlyList<ParsedGradeRow> Rows, string? FileError);

/// <summary>
/// Reads an uploaded grade column (M5 Task 10, L9). <b>Only a plain <c>.xlsx</c> is read</b>: the package must be a zip with a workbook part and must not hold
/// a macro project (<c>vbaProject.bin</c>), an OLE or ActiveX part, or declare a macro-enabled content type (external links and defined names are simply not read). Size, row and column caps apply before and during the
/// read, the whole read has a time budget, a formula cell is an error (its value is never evaluated), and external links and defined names are never followed:
/// only the typed values of the first sheet are read.
/// </summary>
public static class GradeColumnImporter
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    /// <summary>Zip-bomb defence: the package may hold at most this many parts and this many bytes once inflated, and no part may inflate more than <see cref="MaxRatio"/> to 1 (checked by actually inflating, not by the sizes a header claims).</summary>
    public const int MaxEntries = 100;

    public const long MaxUncompressedBytes = 20L * 1024 * 1024;
    public const int MaxRatio = 100;

    /// <summary>A student number is at most this long (the real code length is far shorter); a longer cell is a row error, never echoed.</summary>
    public const int MaxStudentNumberLength = 50;

    private static readonly string[] ForbiddenParts = ["vbaProject.bin", "xl/embeddings/", "xl/activeX/", "xl/macrosheets/"];

    public static ParsedGradeFile Read(Stream file, long maxBytes, CancellationToken ct)
    {
        if (file.Length > maxBytes)
        {
            return Fail("too_large");
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Budget);
        try
        {
            if (!IsPlainXlsx(file))
            {
                return Fail("not_xlsx");
            }

            file.Position = 0;
            using var workbook = new XLWorkbook(file);
            // the sheet named "Grades" (what the export writes), else the first visible sheet: a hidden first sheet is never read
            var sheet = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("Grades", StringComparison.OrdinalIgnoreCase) && w.Visibility == XLWorksheetVisibility.Visible)
                        ?? workbook.Worksheets.FirstOrDefault(w => w.Visibility == XLWorksheetVisibility.Visible);
            if (sheet is null)
            {
                return Fail("empty");
            }

            var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastColumn > GradingLimits.ImportMaxColumns)
            {
                return Fail("too_many_columns");
            }

            int studentColumn = 0, pointsColumn = 0;
            for (var c = 1; c <= lastColumn; c++)
            {
                var header = sheet.Cell(1, c).GetString().Trim();
                if (header.Equals(GradeColumnExporter.StudentNumberHeader, StringComparison.OrdinalIgnoreCase))
                {
                    studentColumn = c;
                }
                else if (header.Equals(GradeColumnExporter.PointsHeader, StringComparison.OrdinalIgnoreCase))
                {
                    pointsColumn = c;
                }
            }

            if (studentColumn == 0 || pointsColumn == 0)
            {
                return Fail("missing_columns");
            }

            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
            if (lastRow - 1 > GradingLimits.ImportMaxRows)
            {
                return Fail("too_many_rows");
            }

            var rows = new List<ParsedGradeRow>();
            for (var r = 2; r <= lastRow; r++)
            {
                budget.Token.ThrowIfCancellationRequested();
                var numberCell = sheet.Cell(r, studentColumn);
                var pointsCell = sheet.Cell(r, pointsColumn);
                var number = numberCell.GetString().Trim();
                if (number.Length == 0 && pointsCell.IsEmpty())
                {
                    continue; // a blank line
                }

                if (numberCell.HasFormula || pointsCell.HasFormula)
                {
                    rows.Add(new ParsedGradeRow(r, number, null, "formula_not_allowed")); // a formula is never evaluated
                    continue;
                }

                rows.Add(ParseRow(r, number, pointsCell));
            }

            return new ParsedGradeFile(rows, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail("time_budget");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail("unreadable");
        }
    }

    private static ParsedGradeRow ParseRow(int row, string number, IXLCell points)
    {
        if (number.Length == 0)
        {
            return new ParsedGradeRow(row, number, null, "student_number_required");
        }

        if (number.Length > MaxStudentNumberLength)
        {
            return new ParsedGradeRow(row, string.Empty, null, "student_number_invalid"); // refused, and never echoed back
        }

        if (points.IsEmpty())
        {
            return new ParsedGradeRow(row, number, null, null);
        }

        if (points.DataType == XLDataType.Number)
        {
            var raw = points.GetDouble();
            return double.IsFinite(raw) && Math.Abs(raw) < 1e9
                ? new ParsedGradeRow(row, number, Convert.ToDecimal(raw, CultureInfo.InvariantCulture), null)
                : new ParsedGradeRow(row, number, null, "points_not_a_number"); // 1E+30 or NaN: a row error, not an unreadable file
        }

        var text = points.GetString().Trim().Replace(',', '.');
        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? new ParsedGradeRow(row, number, value, null)
            : new ParsedGradeRow(row, number, null, "points_not_a_number");
    }

    /// <summary>A zip that holds a workbook and none of the parts that carry code, embedded objects or external references.</summary>
    private static bool IsPlainXlsx(Stream file)
    {
        file.Position = 0;
        Span<byte> magic = stackalloc byte[4];
        if (file.Read(magic) != 4 || magic[0] != 0x50 || magic[1] != 0x4B || magic[2] != 0x03 || magic[3] != 0x04)
        {
            return false; // not a zip: the old binary formats (.xls) and anything else are refused
        }

        file.Position = 0;
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            return false; // a zip header with nothing behind it
        }

        using var _ = zip;
        var names = zip.Entries.Select(e => e.FullName).ToList();
        if (!names.Contains("xl/workbook.xml") || !names.Contains("[Content_Types].xml"))
        {
            return false;
        }

        if (names.Any(n => ForbiddenParts.Any(f => n.Contains(f, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        if (names.Count > MaxEntries || IsBomb(zip))
        {
            return false;
        }

        using var types = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        return !types.ReadToEnd().Contains("macroEnabled", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Inflates every part into nothing, counting bytes: refused when the total passes <see cref="MaxUncompressedBytes"/> or a part inflates more than
    /// <see cref="MaxRatio"/> to 1 (a 450 KB upload can inflate to 150 MB). The count is of bytes really produced, so a header that understates its size does not help.
    /// </summary>
    private static bool IsBomb(ZipArchive zip)
    {
        long total = 0;
        var buffer = new byte[81920];
        foreach (var entry in zip.Entries)
        {
            long size = 0;
            using var stream = entry.Open();
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                size += read;
                total += read;
                if (total > MaxUncompressedBytes)
                {
                    return true;
                }
            }

            if (size > 1024 * 1024 && entry.CompressedLength > 0 && size / entry.CompressedLength > MaxRatio)
            {
                return true;
            }
        }

        return false;
    }

    private static ParsedGradeFile Fail(string code) => new([], code);
}
