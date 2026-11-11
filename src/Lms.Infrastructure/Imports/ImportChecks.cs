using System.Globalization;

namespace Lms.Infrastructure.Imports;

/// <summary>Reusable row checks for definitions.</summary>
public static class ImportChecks
{
    /// <summary>
    /// Rows whose normalized value repeats an earlier row: the later row gets <c>duplicate_in_file</c> pointing at the
    /// first one (arg <c>otherRow</c>). Empty values are ignored.
    /// </summary>
    public static IEnumerable<RowError> DuplicatesInFile(
        IReadOnlyList<ImportRowValues> rows, string columnKey, Func<string?, string?> normalize)
    {
        var first = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = normalize(row.Get(columnKey));
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (first.TryGetValue(key, out var other))
            {
                yield return new RowError(row.RowNumber, columnKey, "duplicate_in_file",
                    new Dictionary<string, string> { ["otherRow"] = other.ToString(CultureInfo.InvariantCulture) });
            }
            else
            {
                first[key] = row.RowNumber;
            }
        }
    }

    /// <summary>Parses an ISO date (<c>yyyy-MM-dd</c>); the reader already renders Excel date cells in that form.</summary>
    public static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>An empty value passes; anything else must be a date, otherwise <c>invalid_date</c>.</summary>
    public static RowError? Date(ImportRowValues row, string key) =>
        string.IsNullOrEmpty(row.Get(key)) || TryParseDate(row.Get(key), out _)
            ? null
            : new RowError(row.RowNumber, key, "invalid_date");

    /// <summary>An empty value passes; a unit code that is not below the target unit gives <c>unknown_unit</c>.</summary>
    public static RowError? UnitCode(ImportRowValues row, string key, ImportValidationContext context) =>
        row.Get(key) is not { Length: > 0 } code || context.UnitsByCode.ContainsKey(code.ToUpperInvariant())
            ? null
            : new RowError(row.RowNumber, key, "unknown_unit", new Dictionary<string, string> { ["value"] = code });
}
