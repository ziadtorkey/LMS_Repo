using Lms.Shared;
using Lms.Shared.Content;

namespace Lms.Client.Services.Api;

/// <summary>Lesson-module assets (FR-CNT-03): limits, upload and signed URLs. Routes: <c>/api/assets</c>.</summary>
public interface IAssetsApi
{
    /// <summary>GET /api/assets/limits?classCourseId=… — what the uploader validates before sending.</summary>
    Task<AssetLimitsDto> GetLimitsAsync(Guid classCourseId, CancellationToken ct = default);

    /// <summary>POST /api/assets (multipart). <paramref name="progress"/> reports the fraction (0..1) of bytes handed to the transport.</summary>
    Task<AssetDto> UploadAsync(Guid classCourseId, string fileName, string contentType, Stream content, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>GET /api/assets/{id}/url — a short-lived signed URL.</summary>
    Task<AssetUrlDto> GetUrlAsync(Guid assetId, CancellationToken ct = default);

    /// <summary>Unit-owned assets (library covers and files, wave 2): limits of the unit.</summary>
    Task<AssetLimitsDto> GetUnitLimitsAsync(Guid orgUnitId, CancellationToken ct = default);

    /// <summary>Unit-owned assets: upload with <c>orgUnitId</c> instead of <c>classCourseId</c> (needs Library.Manage at the unit).</summary>
    Task<AssetDto> UploadToUnitAsync(Guid orgUnitId, string fileName, string contentType, Stream content, IProgress<double>? progress = null, CancellationToken ct = default);
}

public sealed class AssetsApi(ApiHttp http) : IAssetsApi
{
    public Task<AssetLimitsDto> GetLimitsAsync(Guid classCourseId, CancellationToken ct = default) =>
        http.GetAsync<AssetLimitsDto>(Routes.AssetsLimitsFor(classCourseId), ct);

    public Task<AssetDto> UploadAsync(Guid classCourseId, string fileName, string contentType, Stream content, IProgress<double>? progress = null, CancellationToken ct = default) =>
        http.UploadAsync<AssetDto>(Routes.Assets, fileName, contentType, new ProgressStream(content, progress),
            new Dictionary<string, string> { [AssetUploadForm.ClassCourseId] = classCourseId.ToString() }, ct);

    public Task<AssetUrlDto> GetUrlAsync(Guid assetId, CancellationToken ct = default) =>
        http.GetAsync<AssetUrlDto>(Routes.AssetUrl(assetId), ct);

    public Task<AssetLimitsDto> GetUnitLimitsAsync(Guid orgUnitId, CancellationToken ct = default) =>
        http.GetAsync<AssetLimitsDto>(Routes.AssetsLimitsForUnit(orgUnitId), ct);

    public Task<AssetDto> UploadToUnitAsync(Guid orgUnitId, string fileName, string contentType, Stream content, IProgress<double>? progress = null, CancellationToken ct = default) =>
        http.UploadAsync<AssetDto>(Routes.Assets, fileName, contentType, new ProgressStream(content, progress),
            new Dictionary<string, string> { [AssetUploadForm.OrgUnitId] = orgUnitId.ToString() }, ct);
}

/// <summary>Read-only wrapper that reports how much of the stream the transport has consumed.</summary>
internal sealed class ProgressStream(Stream inner, IProgress<double>? progress) : Stream
{
    private long _read;

    public override int Read(byte[] buffer, int offset, int count) => Report(inner.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        Report(await inner.ReadAsync(buffer, ct));

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        Report(await inner.ReadAsync(buffer.AsMemory(offset, count), ct));

    private int Report(int n)
    {
        _read += n;
        if (progress is not null && inner.CanSeek && inner.Length > 0)
            progress.Report(Math.Min(1d, (double)_read / inner.Length));
        return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Standalone dev server and bUnit fake: configurable limits, recorded uploads, signed URLs that expire.</summary>
public sealed class FakeAssetsApi : IAssetsApi
{
    private int _urlCounter;

    public long MaxBytes { get; set; } = 5 * 1024 * 1024;
    public List<(Guid ClassCourseId, string FileName, string ContentType, long Length)> Uploads { get; } = [];
    public List<Guid> UrlRequests { get; } = [];
    private readonly HashSet<Guid> _refused = [];
    /// <summary>The server refuses the signed URL of this asset (a library file of a reader-only resource, D13).</summary>
    public void Refuse(Guid assetId) => _refused.Add(assetId);
    public Exception? Failure { get; set; }
    /// <summary>Failures to throw once each before an upload succeeds (retry tests).</summary>
    public int FailuresLeft { get; set; }
    /// <summary>When set, uploads wait for it (cancel tests).</summary>
    public TaskCompletionSource? Gate { get; set; }
    public TimeSpan UrlLifetime { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>The fake "server clock".</summary>
    public Func<DateTime> ServerNow { get; set; } = () => DateTime.UtcNow;

    public Task<AssetLimitsDto> GetLimitsAsync(Guid classCourseId, CancellationToken ct = default) =>
        Task.FromResult(new AssetLimitsDto(MaxBytes, AssetTypes.All));

    public async Task<AssetDto> UploadAsync(Guid classCourseId, string fileName, string contentType, Stream content, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        progress?.Report(0.5);
        if (Gate is not null) await Gate.Task.WaitAsync(ct);
        if (FailuresLeft > 0) { FailuresLeft--; throw Failure ?? new HttpRequestException("boom"); }
        if (Failure is not null) throw Failure;
        progress?.Report(1);
        Uploads.Add((classCourseId, fileName, contentType, buffer.Length));
        var type = AssetTypes.ForFileName(fileName)!;
        return new AssetDto(Guid.NewGuid(), classCourseId, type.Kind, fileName, contentType, buffer.Length, null, type.Previewable, ServerNow());
    }

    public Task<AssetLimitsDto> GetUnitLimitsAsync(Guid orgUnitId, CancellationToken ct = default) =>
        Task.FromResult(new AssetLimitsDto(MaxBytes, AssetTypes.All));

    public async Task<AssetDto> UploadToUnitAsync(Guid orgUnitId, string fileName, string contentType, Stream content, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var asset = await UploadAsync(Guid.Empty, fileName, contentType, content, progress, ct);
        return asset with { ClassCourseId = null, OrgUnitId = orgUnitId };
    }

    public Task<AssetUrlDto> GetUrlAsync(Guid assetId, CancellationToken ct = default)
    {
        UrlRequests.Add(assetId);
        if (_refused.Contains(assetId)) return Task.FromException<AssetUrlDto>(new ApiProblemException(404, ProblemCodes.not_found, null, null));
        var now = ServerNow();
        return Task.FromResult(new AssetUrlDto($"https://files.test/{assetId}?sig={++_urlCounter}", now + UrlLifetime, now));
    }
}
