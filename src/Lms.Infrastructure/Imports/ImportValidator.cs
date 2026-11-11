using Lms.Domain.Files;
using Lms.Domain.Imports;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Imports;

/// <summary>The outcome of reading and validating an uploaded file; the commit job runs the same steps again.</summary>
public sealed record ImportValidation(
    OrgUnit Target,
    IReadOnlyDictionary<string, OrgUnit> UnitsByCode,
    IReadOnlyList<ImportRowValues> Rows,
    IReadOnlyList<RowError> Errors);

/// <summary>Read the file, apply the framework checks, then the definition's checks.</summary>
public sealed class ImportValidator(LmsDbContext db, IFileStorage files)
{
    public async Task<ImportValidation?> ValidateAsync(ImportBatch batch, IImportDefinition definition, CancellationToken ct)
    {
        // Through the filtered set: runs with the context of the requester, so only units they can still see.
        var target = await db.OrgUnits.FirstOrDefaultAsync(u => u.Id == batch.OrgUnitId, ct);
        if (target is null)
        {
            return null;
        }

        var below = await db.OrgUnits.Where(u => u.Path.StartsWith(target.Path)).ToListAsync(ct);
        var unitsByCode = below.GroupBy(u => u.Code.ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First());

        await using var stream = await files.OpenReadAsync(batch.SourceFileId, ct);
        var read = await ImportReader.ReadAsync(stream, definition.Columns, ct);
        var errors = new List<RowError>(read.FileErrors);
        if (read.FileErrors.Count == 0)
        {
            errors.AddRange(CellChecks(definition.Columns, read.Rows));
            errors.AddRange(await definition.ValidateAsync(
                new ImportValidationContext(batch, target, unitsByCode), read.Rows, ct));
        }

        return new ImportValidation(target, unitsByCode, read.Rows, errors.OrderBy(e => e.RowNumber).ToList());
    }

    private static IEnumerable<RowError> CellChecks(IReadOnlyList<ImportColumn> columns, IReadOnlyList<ImportRowValues> rows)
    {
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var value = row.Get(column.Key);
                if (value is null)
                {
                    if (column.Required)
                    {
                        yield return new RowError(row.RowNumber, column.Key, "required");
                    }

                    continue;
                }

                if (value.Length > ImportReader.MaxCellLength)
                {
                    yield return new RowError(row.RowNumber, column.Key, "too_long");
                }
                else if (column.AllowedValues is { Count: > 0 } allowed
                         && !allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    yield return new RowError(row.RowNumber, column.Key, "not_allowed",
                        new Dictionary<string, string> { ["value"] = value });
                }
            }
        }
    }
}
