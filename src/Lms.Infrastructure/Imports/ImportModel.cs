using Lms.Domain.Imports;
using Lms.Domain.Organizations;

namespace Lms.Infrastructure.Imports;

/// <summary>A template column. Headers match the English header, the Arabic header or the key (case and whitespace ignored).</summary>
public sealed record ImportColumn(
    string Key,
    string HeaderAr,
    string HeaderEn,
    bool Required,
    IReadOnlyList<string>? AllowedValues = null,
    string? ExampleValue = null,
    bool AsText = false,
    bool Sensitive = false)
{
    /// <summary>
    /// A sensitive cell (the student default password, D24) is never stored, previewed or reported: it shows
    /// <see cref="Lms.Shared.Imports.ImportMasks.Sensitive"/> when filled and nothing when empty. Only the commit reads the real value, from the uploaded file.
    /// </summary>
    public static string? Display(ImportColumn column, string? value) =>
        !column.Sensitive ? value : string.IsNullOrEmpty(value) ? null : Lms.Shared.Imports.ImportMasks.Sensitive;
}

/// <summary>One data row as read from the sheet; <c>RowNumber</c> is the Excel row number (data starts at 2).</summary>
public sealed record ImportRowValues(int RowNumber, IReadOnlyDictionary<string, string?> Values)
{
    public string? Get(string key) => Values.TryGetValue(key, out var value) ? value : null;
}

/// <summary>A validation error. <c>RowNumber = 0</c> is a file-level error; <c>ColumnKey</c> null is a whole-row error.</summary>
public sealed record RowError(int RowNumber, string? ColumnKey, string Code, IReadOnlyDictionary<string, string>? Args = null);

public sealed record ImportReadResult(IReadOnlyList<ImportRowValues> Rows, IReadOnlyList<RowError> FileErrors);

/// <summary>What a definition sees while validating: the batch, its target unit and the live units below it by upper-cased code.</summary>
public sealed record ImportValidationContext(
    ImportBatch Batch, OrgUnit TargetUnit, IReadOnlyDictionary<string, OrgUnit> UnitsByCode);

/// <summary>
/// What a definition sees while committing (inside the commit transaction). Work that must happen only after the
/// transaction committed (emails, notifications) is added to <see cref="AfterCommit"/>; it runs once, in order, and a
/// failure there is logged and never undoes the import.
/// </summary>
public sealed record ImportCommitContext(
    ImportBatch Batch, OrgUnit TargetUnit, IReadOnlyDictionary<string, OrgUnit> UnitsByCode,
    IList<Func<CancellationToken, Task>> AfterCommit);

/// <summary>One kind of import (staff, later students). Registered with <c>AddImportDefinition&lt;T&gt;()</c>.</summary>
public interface IImportDefinition
{
    string Kind { get; }

    /// <summary>The permission checked at the target unit by every endpoint of this kind.</summary>
    string Permission { get; }

    IReadOnlyList<ImportColumn> Columns { get; }

    /// <summary>Row-level checks beyond the framework ones (required, allowed values, length).</summary>
    Task<IReadOnlyList<RowError>> ValidateAsync(
        ImportValidationContext context, IReadOnlyList<ImportRowValues> rows, CancellationToken cancellationToken);

    /// <summary>Creates the records inside one transaction that rolls back on any exception. Returns the created count.</summary>
    Task<int> CommitAsync(ImportCommitContext context, IReadOnlyList<ImportRowValues> rows, CancellationToken cancellationToken);
}

/// <summary>Turns an error code and its arguments into text in a culture (implemented by the resource files of the API).</summary>
public interface IImportMessages
{
    string Format(string code, string culture, IReadOnlyDictionary<string, string>? args);
}
