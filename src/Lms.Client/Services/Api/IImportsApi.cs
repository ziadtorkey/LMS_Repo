using Lms.Shared;
using Lms.Shared.Imports;
using Microsoft.AspNetCore.Components.Forms;

namespace Lms.Client.Services.Api;

/// <summary>Excel imports of every kind (X-06): template, upload, validated preview, all-or-nothing commit, history.</summary>
public interface IImportsApi
{
    Task<IReadOnlyList<ImportColumnDto>> GetColumnsAsync(string kind, CancellationToken ct = default);
    Task<(Stream Content, string FileName)> DownloadTemplateAsync(string kind, string culture, CancellationToken ct = default);
    Task<ImportBatchDto> UploadAsync(string kind, Guid orgUnitId, IBrowserFile file, CancellationToken ct = default);
    Task<ImportBatchDto> GetBatchAsync(string kind, Guid id, CancellationToken ct = default);

    /// <summary><paramref name="page"/> is 1-based, like every paged list of the API.</summary>
    Task<PagedResult<ImportRowDto>> GetRowsAsync(
        string kind, Guid id, bool errorsOnly, string culture, int page, int pageSize, CancellationToken ct = default);

    Task<(Stream Content, string FileName)> DownloadErrorReportAsync(string kind, Guid id, CancellationToken ct = default);
    /// <summary>The one-time credentials sheet of a committed student import (D24); available once, to the requester, for 15 minutes.</summary>
    Task<(Stream Content, string FileName)> DownloadCredentialsAsync(string kind, Guid id, CancellationToken ct = default);

    Task<ImportBatchDto> CommitAsync(string kind, Guid id, CancellationToken ct = default);
    Task DiscardAsync(string kind, Guid id, CancellationToken ct = default);
    Task<PagedResult<ImportBatchDto>> ListAsync(string kind, int page, int pageSize, CancellationToken ct = default);
}
