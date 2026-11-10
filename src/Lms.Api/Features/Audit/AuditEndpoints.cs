using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Auditing;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Audit;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.AuditExport, ExportAsync).RequireAuthorization().RequirePermission(Permissions.Audit.Export);
        app.MapGet(Routes.Audit, QueryAsync).RequireAuthorization().RequirePermission(Permissions.Audit.View);
        app.MapGet(Routes.AuditEntryPattern, GetAsync).RequireAuthorization().RequirePermission(Permissions.Audit.View);
        return app;
    }

    private static async Task<IResult> QueryAsync(
        [AsParameters] AuditQuery query, LmsDbContext db, ICurrentContext context, CancellationToken ct)
    {
        if (InvalidAction(query) is { } invalid)
        {
            return invalid;
        }

        var (page, pageSize) = AuditQueryBuilder.NormalizePaging(query);
        var filtered = AuditQueryBuilder.Build(db.AuditEntries.AsNoTracking(), query, context);
        var total = await filtered.CountAsync(ct);
        var entries = await filtered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var rows = await AuditRows.ToDtosAsync(db, entries, ct);
        return Results.Ok(new PagedResult<AuditEntryDto>(rows, total, page, pageSize));
    }

    private static async Task<IResult> GetAsync(Guid id, LmsDbContext db, ICurrentContext context, CancellationToken ct)
    {
        var none = new AuditQuery(null, null, null, null, null, null, null);
        var entry = await AuditQueryBuilder.Visible(db.AuditEntries.AsNoTracking(), none, context)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entry is null)
        {
            return ApiProblems.NotFound();
        }

        var row = (await AuditRows.ToDtosAsync(db, [entry], ct))[0];
        return Results.Ok(new AuditEntryDetailDto(row, entry.BeforeJson, entry.AfterJson));
    }

    private static async Task<IResult> ExportAsync(
        [AsParameters] AuditQuery query, [FromQuery] string? format, LmsDbContext db, ICurrentContext context,
        AuditExportLimit limit, IClock clock, CancellationToken ct)
    {
        if (InvalidAction(query) is { } invalidAction)
        {
            return invalidAction;
        }

        var isCsv = string.Equals(format, AuditExportFormats.Csv, StringComparison.OrdinalIgnoreCase);
        if (!isCsv && !string.Equals(format, AuditExportFormats.Xlsx, StringComparison.OrdinalIgnoreCase))
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "format must be csv or xlsx.");
        }

        var filtered = AuditQueryBuilder.Build(db.AuditEntries.AsNoTracking(), query, context);
        if (await filtered.CountAsync(ct) > limit.MaxRows)
        {
            return ApiProblems.BadRequest(ProblemCodes.export_too_large, $"An export holds at most {limit.MaxRows} rows; narrow the filters.");
        }

        var entries = await filtered.Take(limit.MaxRows).ToListAsync(ct);
        var rows = await AuditRows.ToDtosAsync(db, entries, ct);
        var stamp = clock.UtcNow.ToString("yyyyMMdd-HHmm");

        // Built in memory: bounded by the row limit, and lets a failure still return a clean problem response.
        using var buffer = new MemoryStream();
        if (isCsv)
        {
            await AuditExporter.WriteCsvAsync(rows, buffer, ct);
        }
        else
        {
            AuditExporter.WriteXlsx(rows, buffer);
        }

        return Results.File(
            buffer.ToArray(),
            isCsv ? "text/csv; charset=utf-8" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"audit-{stamp}.{(isCsv ? "csv" : "xlsx")}");
    }

    private static IResult? InvalidAction(AuditQuery query) =>
        string.IsNullOrWhiteSpace(query.Action) || AuditActions.All.Contains(query.Action, StringComparer.OrdinalIgnoreCase)
            ? null
            : ApiProblems.BadRequest(ProblemCodes.validation_failed, "Unknown action filter.");
}
