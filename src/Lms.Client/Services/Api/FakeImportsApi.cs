using System.Text;
using Lms.Shared;
using Lms.Shared.Imports;
using Microsoft.AspNetCore.Components.Forms;

namespace Lms.Client.Services.Api;

/// <summary>
/// In-memory fake for the standalone dev server (Lms:UseFakeApi=true). A batch moves Uploaded, Validating, Valid on
/// successive reads; a file whose name contains "bad" ends Invalid (with row errors); a commit ends Committed.
/// </summary>
public sealed class FakeImportsApi : IImportsApi
{
    private const int FakeRows = 120;

    private static readonly IReadOnlyList<ImportColumnDto> StaffColumns =
    [
        new("email", "البريد الإلكتروني", "Email", true, null),
        new("nameAr", "الاسم بالعربية", "Name (Arabic)", true, null),
        new("nameEn", "الاسم بالإنجليزية", "Name (English)", false, null),
        new("jobTitle", "المسمى الوظيفي", "Job title", false, null),
    ];

    private readonly List<ImportBatchDto> _batches = [];

    public Task<IReadOnlyList<ImportColumnDto>> GetColumnsAsync(string kind, CancellationToken ct = default) =>
        Task.FromResult(StaffColumns);

    public Task<(Stream Content, string FileName)> DownloadTemplateAsync(string kind, string culture, CancellationToken ct = default) =>
        Task.FromResult<(Stream, string)>((new MemoryStream(Encoding.UTF8.GetBytes("template")), $"{kind}-template-{culture}.xlsx"));

    public Task<ImportBatchDto> UploadAsync(string kind, Guid orgUnitId, IBrowserFile file, CancellationToken ct = default)
    {
        var batch = new ImportBatchDto(Guid.NewGuid(), kind, ImportStatuses.Uploaded, orgUnitId, "مدرسة النور", "Al Noor School",
            file.Name, FakeRows, 0, false, null, Guid.NewGuid(), "مدير النظام", "Administrator", DateTime.UtcNow, null);
        _batches.Insert(0, batch);
        return Task.FromResult(batch);
    }

    public Task<ImportBatchDto> GetBatchAsync(string kind, Guid id, CancellationToken ct = default)
    {
        var index = _batches.FindIndex(b => b.Id == id);
        var batch = _batches[index];
        var bad = batch.FileName.Contains("bad", StringComparison.OrdinalIgnoreCase);
        batch = batch.Status switch
        {
            ImportStatuses.Uploaded => batch with { Status = ImportStatuses.Validating },
            ImportStatuses.Validating when bad => batch with { Status = ImportStatuses.Invalid, ErrorCount = 3, HasErrorReport = true },
            ImportStatuses.Validating => batch with { Status = ImportStatuses.Valid },
            ImportStatuses.Committing => batch with
            {
                Status = ImportStatuses.Committed, CreatedCount = batch.RowCount, CommittedAt = DateTime.UtcNow,
            },
            _ => batch,
        };
        _batches[index] = batch;
        return Task.FromResult(batch);
    }

    public Task<PagedResult<ImportRowDto>> GetRowsAsync(
        string kind, Guid id, bool errorsOnly, string culture, int page, int pageSize, CancellationToken ct = default)
    {
        var batch = _batches.First(b => b.Id == id);
        var rows = Enumerable.Range(2, batch.RowCount).Select(n => Row(n, batch.ErrorCount > 0 && n <= 4)).ToList();
        var source = errorsOnly ? rows.Where(r => r.Errors.Count > 0).ToList() : rows;
        var items = source.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new PagedResult<ImportRowDto>(items, source.Count, page, pageSize));
    }

    public Task<(Stream Content, string FileName)> DownloadErrorReportAsync(string kind, Guid id, CancellationToken ct = default) =>
        Task.FromResult<(Stream, string)>((new MemoryStream(Encoding.UTF8.GetBytes("errors")), "errors.xlsx"));

    public Task<ImportBatchDto> CommitAsync(string kind, Guid id, CancellationToken ct = default)
    {
        var index = _batches.FindIndex(b => b.Id == id);
        _batches[index] = _batches[index] with { Status = ImportStatuses.Committing };
        return Task.FromResult(_batches[index]);
    }

    public Task DiscardAsync(string kind, Guid id, CancellationToken ct = default)
    {
        _batches.RemoveAll(b => b.Id == id);
        return Task.CompletedTask;
    }

    public Task<PagedResult<ImportBatchDto>> ListAsync(string kind, int page, int pageSize, CancellationToken ct = default)
    {
        var all = _batches.Where(b => b.Kind == kind).ToList();
        return Task.FromResult(new PagedResult<ImportBatchDto>(all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), all.Count, page, pageSize));
    }

    private static ImportRowDto Row(int number, bool failing) => new(number,
        new Dictionary<string, string?>
        {
            ["email"] = failing ? "not-an-email" : $"staff{number}@example.com",
            ["nameAr"] = $"موظف {number}",
            ["nameEn"] = $"Staff {number}",
            ["jobTitle"] = null,
        },
        failing ? [new ImportRowErrorDto("email", "invalid_email", "Invalid email address")] : []);
}
