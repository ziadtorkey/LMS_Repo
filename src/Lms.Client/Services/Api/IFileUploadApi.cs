namespace Lms.Client.Services.Api;

/// <summary>Uploads one file (POST /api/files) and returns its id. Used by the people photo component.</summary>
public interface IFileUploadApi
{
    Task<Guid> UploadAsync(string fileName, string contentType, Stream content, Guid orgUnitId, CancellationToken ct = default);

    /// <summary>Short-lived download URL of a stored file (GET /api/files/{id}/url).</summary>
    Task<string> GetUrlAsync(Guid fileId, CancellationToken ct = default);
}

/// <summary>The part of the upload response the client needs.</summary>
public sealed record FileUrlDto(string Url, DateTime ExpiresAtUtc);

public sealed record UploadedFileDto(Guid Id, string FileName, string ContentType, long SizeBytes, Guid OrgUnitId);

public sealed class FileUploadApi(ApiHttp http) : IFileUploadApi
{
    public async Task<Guid> UploadAsync(string fileName, string contentType, Stream content, Guid orgUnitId, CancellationToken ct = default) =>
        (await http.UploadAsync<UploadedFileDto>("/api/files", fileName, contentType, content,
            new Dictionary<string, string> { ["orgUnitId"] = orgUnitId.ToString() }, ct)).Id;

    public async Task<string> GetUrlAsync(Guid fileId, CancellationToken ct = default) =>
        (await http.GetAsync<FileUrlDto>($"/api/files/{fileId}/url", ct)).Url;
}

public sealed class FakeFileUploadApi : IFileUploadApi
{
    public List<(string FileName, string ContentType, long Length, Guid OrgUnitId)> Uploads { get; } = [];
    public Exception? Failure { get; set; }
    public Exception? UrlFailure { get; set; }

    public Task<string> GetUrlAsync(Guid fileId, CancellationToken ct = default) =>
        UrlFailure is not null
            ? Task.FromException<string>(UrlFailure)
            : Task.FromResult($"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='8' height='8'/%3E#{fileId}");

    public async Task<Guid> UploadAsync(string fileName, string contentType, Stream content, Guid orgUnitId, CancellationToken ct = default)
    {
        if (Failure is not null) throw Failure;
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        Uploads.Add((fileName, contentType, buffer.Length, orgUnitId));
        return Guid.NewGuid();
    }
}
