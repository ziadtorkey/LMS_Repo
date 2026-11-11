namespace Lms.Client.Services.Api;

/// <summary>Uploads one file (POST /api/files) and returns its id. Used by the people photo component.</summary>
public interface IFileUploadApi
{
    Task<Guid> UploadAsync(string fileName, string contentType, Stream content, Guid orgUnitId, CancellationToken ct = default);
}

/// <summary>The part of the upload response the client needs.</summary>
public sealed record UploadedFileDto(Guid Id, string FileName, string ContentType, long SizeBytes, Guid OrgUnitId);

public sealed class FileUploadApi(ApiHttp http) : IFileUploadApi
{
    public async Task<Guid> UploadAsync(string fileName, string contentType, Stream content, Guid orgUnitId, CancellationToken ct = default) =>
        (await http.UploadAsync<UploadedFileDto>("/api/files", fileName, contentType, content,
            new Dictionary<string, string> { ["orgUnitId"] = orgUnitId.ToString() }, ct)).Id;
}

public sealed class FakeFileUploadApi : IFileUploadApi
{
    public List<(string FileName, string ContentType, long Length, Guid OrgUnitId)> Uploads { get; } = [];
    public Exception? Failure { get; set; }

    public async Task<Guid> UploadAsync(string fileName, string contentType, Stream content, Guid orgUnitId, CancellationToken ct = default)
    {
        if (Failure is not null) throw Failure;
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        Uploads.Add((fileName, contentType, buffer.Length, orgUnitId));
        return Guid.NewGuid();
    }
}
