using System.Text.Json;
using Lms.Domain.Imports;
using Lms.Infrastructure.Imports;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Imports;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Imports;

/// <summary>Reads batches and rows through the filtered sets (caller subtree, client, soft delete) and maps them to DTOs.</summary>
public sealed class ImportQueries(LmsDbContext db, IImportMessages messages)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ImportBatch?> FindAsync(string kind, Guid id, CancellationToken ct) =>
        db.Set<ImportBatch>().FirstOrDefaultAsync(b => b.Id == id && b.Kind == kind, ct);

    public async Task<PagedResult<ImportBatchDto>> ListAsync(string kind, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Set<ImportBatch>().Where(b => b.Kind == kind);
        var total = await query.CountAsync(ct);
        var batches = await query.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<ImportBatchDto>(await ToDtosAsync(batches, ct), total, page, pageSize);
    }

    public async Task<ImportBatchDto> ToDtoAsync(ImportBatch batch, CancellationToken ct) =>
        (await ToDtosAsync([batch], ct))[0];

    private async Task<IReadOnlyList<ImportBatchDto>> ToDtosAsync(IReadOnlyList<ImportBatch> batches, CancellationToken ct)
    {
        var unitIds = batches.Select(b => b.OrgUnitId).Distinct().ToList();
        var units = await db.OrgUnits.Where(u => unitIds.Contains(u.Id))
            .Select(u => new { u.Id, u.NameAr, u.NameEn }).ToDictionaryAsync(u => u.Id, ct);
        var userIds = batches.Select(b => b.RequestedBy).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayNameAr, u.DisplayNameEn }).ToDictionaryAsync(u => u.Id, ct);

        return batches.Select(b =>
        {
            units.TryGetValue(b.OrgUnitId, out var unit);
            users.TryGetValue(b.RequestedBy, out var user);
            return new ImportBatchDto(
                b.Id, b.Kind, b.Status.ToString(), b.OrgUnitId, unit?.NameAr ?? string.Empty, unit?.NameEn ?? string.Empty,
                b.FileName, b.RowCount, b.ErrorCount, b.ErrorReportFileId is not null, b.CreatedCount, b.RequestedBy,
                user?.DisplayNameAr, user?.DisplayNameEn, b.CreatedAt, b.CommittedAt);
        }).ToList();
    }

    public async Task<PagedResult<ImportRowDto>> RowsAsync(
        ImportBatch batch, bool errorsOnly, string culture, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Set<ImportRow>().Where(r => r.BatchId == batch.Id);
        if (errorsOnly)
        {
            query = query.Where(r => r.ErrorsJson != null);
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(r => r.RowNumber).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(r => new ImportRowDto(
            r.RowNumber,
            JsonSerializer.Deserialize<Dictionary<string, string?>>(r.ValuesJson) ?? [],
            r.ErrorsJson is null
                ? []
                : (JsonSerializer.Deserialize<List<ImportJobBase.StoredError>>(r.ErrorsJson) ?? [])
                    .Select(e => new ImportRowErrorDto(e.Column, e.Code, messages.Format(e.Code, culture, e.Args))).ToList()))
            .ToList();
        return new PagedResult<ImportRowDto>(items, total, page, pageSize);
    }
}
