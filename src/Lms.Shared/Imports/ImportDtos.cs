namespace Lms.Shared.Imports;

/// <summary>Import kinds; each value is the <c>{kind}</c> route segment. <c>Students</c> arrives in M3 (D23).</summary>
public static class ImportKinds
{
    public const string Staff = "staff";

    public static IReadOnlyList<string> All { get; } = [Staff];
}

/// <summary>Lifecycle of an import batch (X-06): upload → validate job → preview → all-or-nothing commit job.</summary>
public static class ImportStatuses
{
    public const string Uploaded = "Uploaded";
    public const string Validating = "Validating";
    public const string Valid = "Valid";
    public const string Invalid = "Invalid";
    public const string Committing = "Committing";
    public const string Committed = "Committed";
    public const string Failed = "Failed";

    public static IReadOnlyList<string> All { get; } =
        [Uploaded, Validating, Valid, Invalid, Committing, Committed, Failed];
}

/// <summary>File limits of every import (D34): <c>.xlsx</c> only, first worksheet, header row 1.</summary>
public static class ImportLimits
{
    public const int MaxRows = 5000;
    public const int MaxFileBytes = 10 * 1024 * 1024;
}

/// <summary>A template column. Headers match the English header, Arabic header or key (case/whitespace-insensitive).</summary>
public sealed record ImportColumnDto(
    string Key,
    string HeaderAr,
    string HeaderEn,
    bool Required,
    IReadOnlyList<string>? AllowedValues);

/// <summary>An import batch. <c>CreatedCount</c> is set once committed.</summary>
public sealed record ImportBatchDto(
    Guid Id,
    string Kind,
    string Status,
    Guid OrgUnitId,
    string OrgUnitNameAr,
    string OrgUnitNameEn,
    string FileName,
    int RowCount,
    int ErrorCount,
    bool HasErrorReport,
    int? CreatedCount,
    Guid RequestedBy,
    string? RequestedByNameAr,
    string? RequestedByNameEn,
    DateTime CreatedAt,
    DateTime? CommittedAt);

/// <summary>One validation error of a row; <c>Column</c> is a column key or null for a whole-row error.</summary>
public sealed record ImportRowErrorDto(string? Column, string Code, string Message);

/// <summary>One data row of a batch (row numbers are worksheet row numbers, data starting at 2).</summary>
public sealed record ImportRowDto(
    int RowNumber,
    IReadOnlyDictionary<string, string?> Values,
    IReadOnlyList<ImportRowErrorDto> Errors);
