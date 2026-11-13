using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.Packages;

namespace Lms.Client.Services.Packages;

/// <summary>SCORM / H5P packages (FR-STD-01, FR-STD-02): upload, validation status, attach to a lesson.</summary>
public interface IPackagesApi
{
    /// <summary>
    /// POST /api/class-courses/{id}/packages (multipart) → 202 with the upload in state Uploaded. The lesson is required: the package
    /// is anchored to it from the upload on. <paramref name="contentLength"/> lets progress be reported for a stream that cannot seek.
    /// </summary>
    Task<PackageUploadDto> UploadAsync(Guid classCourseId, string kind, Guid lessonKey, string fileName, Stream content,
        IProgress<double>? progress = null, CancellationToken ct = default, long? contentLength = null);

    /// <summary>GET /api/packages/{id} — the current check state.</summary>
    Task<PackageUploadDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// GET /api/class-courses/{id}/packages?lessonKey=&amp;status= — uploads not attached yet (the "Checking…" rows). <paramref name="status"/>
    /// is a <see cref="PackageUploadStatuses"/> value; null lists every status.
    /// </summary>
    Task<IReadOnlyList<PackageUploadDto>> ListPendingAsync(Guid classCourseId, Guid? lessonKey = null, string? status = null,
        CancellationToken ct = default);

    /// <summary>POST /api/packages/{id}/module — the new Scorm / H5p module.</summary>
    Task<ModuleNodeDto> AttachAsync(Guid id, AttachPackageModuleRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class PackagesApi(ApiHttp http) : IPackagesApi
{
    public Task<PackageUploadDto> UploadAsync(Guid classCourseId, string kind, Guid lessonKey, string fileName, Stream content,
        IProgress<double>? progress = null, CancellationToken ct = default, long? contentLength = null)
    {
        var fields = new Dictionary<string, string>
        {
            [PackageUploadForm.LessonKey] = lessonKey.ToString(),
            [PackageUploadForm.Kind] = kind,
        };
        // An .h5p is not a zip as far as the browser and the server are concerned: it gets its own (generic) content type.
        var contentType = kind == PackageKinds.H5p ? "application/octet-stream" : "application/zip";
        return http.UploadAsync<PackageUploadDto>(Routes.ClassCoursePackages(classCourseId), fileName, contentType,
            new ProgressStream(content, progress, contentLength), fields, ct);
    }

    public Task<PackageUploadDto> GetAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<PackageUploadDto>(Routes.Package(id), ct);

    public Task<IReadOnlyList<PackageUploadDto>> ListPendingAsync(Guid classCourseId, Guid? lessonKey = null, string? status = null,
        CancellationToken ct = default)
    {
        var route = lessonKey is { } key ? Routes.ClassCoursePackagesOfLesson(classCourseId, key) : Routes.ClassCoursePackages(classCourseId);
        if (status is not null) route += (lessonKey is null ? "?" : "&") + "status=" + Uri.EscapeDataString(status);
        return http.GetAsync<IReadOnlyList<PackageUploadDto>>(route, ct);
    }

    public async Task<ModuleNodeDto> AttachAsync(Guid id, AttachPackageModuleRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ModuleNodeDto>(HttpMethod.Post, Routes.PackageModule(id), request, ct))!;

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.Package(id), null, ct);
}

/// <summary>
/// In-memory packages. The check takes <see cref="ChecksBeforeReady"/> reads (Uploaded, Validating, then Ready); a file name
/// containing a reject reason code (for example "nested_archive.zip") is rejected with it; <see cref="FeatureDisabled"/> and
/// <see cref="HostNotConfigured"/> reproduce the blocking states.
/// </summary>
public sealed class FakePackagesApi : IPackagesApi
{
    private readonly Dictionary<Guid, (PackageUploadDto Dto, int Reads)> _items = [];

    public int ChecksBeforeReady { get; set; } = 2;
    public bool FeatureDisabled { get; set; }
    public bool HostNotConfigured { get; set; }
    public Exception? Failure { get; set; }
    /// <summary>Raised by the attach call only.</summary>
    public Exception? AttachFailure { get; set; }
    public List<AttachPackageModuleRequest> Attached { get; } = [];
    public List<Guid> Deleted { get; } = [];
    public List<(Guid ClassCourseId, string Kind, Guid LessonKey, string FileName)> Uploads { get; } = [];
    /// <summary>Raised by the list read (the open-time check of the blocking states); null = fine.</summary>
    public Exception? ListFailure { get; set; }
    public int ListReads { get; private set; }
    public int StatusReads { get; private set; }

    public async Task<PackageUploadDto> UploadAsync(Guid classCourseId, string kind, Guid lessonKey, string fileName, Stream content,
        IProgress<double>? progress = null, CancellationToken ct = default, long? contentLength = null)
    {
        Guard();
        var buffer = new byte[8192];
        while (await content.ReadAsync(buffer, ct) > 0) { }
        progress?.Report(1);
        Uploads.Add((classCourseId, kind, lessonKey, fileName));
        var dto = new PackageUploadDto(Guid.NewGuid(), classCourseId, lessonKey, fileName, kind, PackageUploadStatuses.Uploaded, null, null, null, null, null, DateTime.UtcNow);
        _items[dto.Id] = (dto, 0);
        return dto;
    }

    public Task<PackageUploadDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        if (Failure is not null) return Task.FromException<PackageUploadDto>(Failure);
        StatusReads++;
        if (!_items.TryGetValue(id, out var item))
            return Task.FromException<PackageUploadDto>(new ApiProblemException(404, ProblemCodes.not_found, null, null));
        var reads = item.Reads + 1;
        var reason = PackageRejectReasons.All.FirstOrDefault(r => item.Dto.FileName.Contains(r, StringComparison.OrdinalIgnoreCase));
        var dto = item.Dto;
        if (reason is not null && reads >= 2)
            dto = dto with { Status = PackageUploadStatuses.Rejected, RejectReason = reason };
        else if (reason is null && reads > ChecksBeforeReady)
            dto = dto with
            {
                Status = PackageUploadStatuses.Ready,
                Title = Path.GetFileNameWithoutExtension(dto.FileName),
                Version = dto.Kind == PackageKinds.Scorm ? "2004" : null,
                MainLibrary = dto.Kind == PackageKinds.H5p ? "H5P.InteractiveVideo" : null,
            };
        else
            dto = dto with { Status = PackageUploadStatuses.Validating };
        _items[id] = (dto, reads);
        return Task.FromResult(dto);
    }

    public Task<IReadOnlyList<PackageUploadDto>> ListPendingAsync(Guid classCourseId, Guid? lessonKey = null, string? status = null,
        CancellationToken ct = default)
    {
        ListReads++;
        if (ListFailure is not null) return Task.FromException<IReadOnlyList<PackageUploadDto>>(ListFailure);
        return Task.FromResult<IReadOnlyList<PackageUploadDto>>(_items.Values.Select(i => i.Dto)
            .Where(d => d.ClassCourseId == classCourseId && (lessonKey is null || d.LessonKey == lessonKey) && (status is null || d.Status == status)).ToList());
    }

    public Task<ModuleNodeDto> AttachAsync(Guid id, AttachPackageModuleRequest request, CancellationToken ct = default)
    {
        if (AttachFailure is not null) return Task.FromException<ModuleNodeDto>(AttachFailure);
        if (!PackageGradingMethods.All.Contains(request.GradingMethod))   // as the server: any other value is 400 validation_failed (D57)
            return Task.FromException<ModuleNodeDto>(new ApiProblemException(400, ProblemCodes.validation_failed, null, null));
        if (Failure is not null) return Task.FromException<ModuleNodeDto>(Failure);
        if (!_items.TryGetValue(id, out var item))
            return Task.FromException<ModuleNodeDto>(new ApiProblemException(404, ProblemCodes.not_found, null, null));
        if (item.Dto.Status != PackageUploadStatuses.Ready)
            return Task.FromException<ModuleNodeDto>(new ApiProblemException(409, "package_not_ready", null, null));
        Attached.Add(request);
        _items.Remove(id);
        var s = request.Module;
        return Task.FromResult(new ModuleNodeDto(Guid.NewGuid(), item.Dto.Kind == PackageKinds.Scorm ? ModuleKinds.Scorm : ModuleKinds.H5p, 1,
            s.TitleAr, s.TitleEn, s.Visibility, s.VisibleFromUtc, s.VisibleToUtc, s.Target, s.GroupId, null, null, s.CompletionRule, true, null));
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        Deleted.Add(id);
        _items.Remove(id);
        return Task.CompletedTask;
    }

    private void Guard()
    {
        if (FeatureDisabled) throw new ApiProblemException(403, "feature_disabled", null, null);
        if (HostNotConfigured) throw new ApiProblemException(409, "package_host_not_configured", null, null);
        if (Failure is not null) throw Failure;
    }
}

/// <summary>How the upload dialog polls the check: the first delay, the growth, the longest delay and the number of polls before "Check again".</summary>
public sealed class PackagePollOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(2);
    public double Backoff { get; set; } = 1.5;
    public TimeSpan MaxInterval { get; set; } = TimeSpan.FromSeconds(10);
    public int MaxPolls { get; set; } = 30;
    /// <summary>Waits between two polls; tests replace it with a gate they open by hand.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (span, ct) => Task.Delay(span, ct);
}
