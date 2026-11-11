using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Imports;
using Microsoft.AspNetCore.Components.Forms;

namespace Lms.Client.Tests.Support;

/// <summary>Scriptable imports API: <see cref="Statuses"/> are served one per <c>GetBatchAsync</c> call (the last one repeats).</summary>
public sealed class StubImportsApi : IImportsApi
{
    private int _reads;

    public static readonly IReadOnlyList<ImportColumnDto> Columns =
    [
        new("email", "البريد", "Email", true, null),
        new("nameEn", "الاسم", "Name", false, null),
    ];

    public List<string> TemplateCultures { get; } = [];
    public List<(Guid UnitId, string FileName)> Uploads { get; } = [];
    public List<(bool ErrorsOnly, int Page)> RowRequests { get; } = [];
    public List<Guid> Committed { get; } = [];
    public List<Guid> Discarded { get; } = [];
    public string? LastKind { get; private set; }
    public Guid UnitId { get; } = Guid.NewGuid();
    public int ErrorReports { get; private set; }
    public int BatchReads => _reads;

    /// <summary>Statuses returned by successive reads of the batch.</summary>
    public List<string> Statuses { get; set; } = [ImportStatuses.Valid];

    public int RowCount { get; set; } = 3;
    public int ErrorCount { get; set; }
    public IReadOnlyList<ImportRowDto> Rows { get; set; } = [];

    public Task<IReadOnlyList<ImportColumnDto>> GetColumnsAsync(string kind, CancellationToken ct = default) =>
        Task.FromResult(Columns);

    public Task<(Stream Content, string FileName)> DownloadTemplateAsync(string kind, string culture, CancellationToken ct = default)
    {
        LastKind = kind;
        TemplateCultures.Add(culture);
        return Task.FromResult<(Stream, string)>((new MemoryStream([1]), "template.xlsx"));
    }

    public Task<ImportBatchDto> UploadAsync(string kind, Guid orgUnitId, IBrowserFile file, CancellationToken ct = default)
    {
        Uploads.Add((orgUnitId, file.Name));
        return Task.FromResult(Batch(ImportStatuses.Uploaded));
    }

    public Task<ImportBatchDto> GetBatchAsync(string kind, Guid id, CancellationToken ct = default)
    {
        var status = Statuses[Math.Min(_reads, Statuses.Count - 1)];
        _reads++;
        return Task.FromResult(Batch(status));
    }

    public Task<PagedResult<ImportRowDto>> GetRowsAsync(
        string kind, Guid id, bool errorsOnly, string culture, int page, int pageSize, CancellationToken ct = default)
    {
        RowRequests.Add((errorsOnly, page));
        var rows = errorsOnly ? Rows.Where(r => r.Errors.Count > 0).ToList() : Rows.ToList();
        return Task.FromResult(new PagedResult<ImportRowDto>(rows, rows.Count, page, pageSize));
    }

    public Task<(Stream Content, string FileName)> DownloadErrorReportAsync(string kind, Guid id, CancellationToken ct = default)
    {
        ErrorReports++;
        return Task.FromResult<(Stream, string)>((new MemoryStream([1]), "errors.xlsx"));
    }

    public Task<ImportBatchDto> CommitAsync(string kind, Guid id, CancellationToken ct = default)
    {
        Committed.Add(id);
        Statuses = [ImportStatuses.Committing, ImportStatuses.Committed];
        _reads = 0;
        return Task.FromResult(Batch(ImportStatuses.Committing));
    }

    public Task DiscardAsync(string kind, Guid id, CancellationToken ct = default)
    {
        Discarded.Add(id);
        return Task.CompletedTask;
    }

    public Task<PagedResult<ImportBatchDto>> ListAsync(string kind, int page, int pageSize, CancellationToken ct = default) =>
        Task.FromResult(new PagedResult<ImportBatchDto>([Batch(ImportStatuses.Committed)], 1, page, pageSize));

    public ImportBatchDto Batch(string status) => new(
        BatchId, ImportKinds.Staff, status, UnitId, "مدرسة", "School", "staff.xlsx", RowCount, ErrorCount,
        ErrorCount > 0, status == ImportStatuses.Committed ? RowCount : null, Guid.NewGuid(), null, null, DateTime.UtcNow, null);

    public Guid BatchId { get; } = Guid.NewGuid();
}
