namespace Lms.Infrastructure.Spreadsheets;

/// <summary>
/// The one place that makes a text value safe for a spreadsheet cell or a CSV field (M5 L9; moved here from <c>AuditExporter</c>, not copied):
/// a value that starts like a formula (<c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage return) gets a leading apostrophe, so Excel never
/// evaluates it. Every export of user-typed text goes through <see cref="Safe"/>.
/// </summary>
public static class SpreadsheetCell
{
    public static string Safe(string? value)
    {
        var text = value ?? string.Empty;
        return text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;
    }
}
