using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.Packages;

namespace Lms.Client.Services.Packages;

/// <summary>SCORM / H5P packages (FR-STD-01, FR-STD-02): upload, validation status, attach to a lesson.</summary>
public interface IPackagesApi
{
    /// <summary>POST /api/class-courses/{id}/packages (multipart) → 202 with the upload in state Uploaded.</summary>
    Task<PackageUploadDto> UploadAsync(Guid classCourseId, string kind, Guid? lessonKey, string fileName, Stream content,
        IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>GET /api/packages/{id} — the current check state.</summary>
    Task<PackageUploadDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Uploads of the class course that are not attached yet (the "Checking…" rows of the lesson).</summary>
    Task<IReadOnlyList<PackageUploadDto>> ListPendingAsync(Guid classCourseId, CancellationToken ct = default);

    /// <summary>POST /api/packages/{id}/module — the new Scorm / H5p module.</summary>
    Task<ModuleNodeDto> AttachAsync(Guid id, AttachPackageModuleRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class PackagesApi(ApiHttp http) : IPackagesApi
{
    public Task<PackageUploadDto> UploadAsync(Guid classCourseId, string kind, Guid? lessonKey, string fileName, Stream content,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var fields = new Dictionary<string, string> { ["kind"] = kind };
        if (lessonKey is { } key) fields["lessonKey"] = key.ToString();
        return http.UploadAsync<PackageUploadDto>(PackageRoutes.Upload(classCourseId), fileName, "application/zip",
            new ProgressStream(content, progress), fields, ct);
    }

    public Task<PackageUploadDto> GetAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<PackageUploadDto>(PackageRoutes.One(id), ct);

    public Task<IReadOnlyList<PackageUploadDto>> ListPendingAsync(Guid classCourseId, CancellationToken ct = default) =>
        http.GetAsync<IReadOnlyList<PackageUploadDto>>(PackageRoutes.Pending(classCourseId), ct);

    public async Task<ModuleNodeDto> AttachAsync(Guid id, AttachPackageModuleRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ModuleNodeDto>(HttpMethod.Post, PackageRoutes.Attach(id), request, ct))!;

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, PackageRoutes.One(id), null, ct);
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
    public List<AttachPackageModuleRequest> Attached { get; } = [];
    public List<Guid> Deleted { get; } = [];
    public List<(Guid ClassCourseId, string Kind, Guid? LessonKey, string FileName)> Uploads { get; } = [];
    public int StatusReads { get; private set; }

    public async Task<PackageUploadDto> UploadAsync(Guid classCourseId, string kind, Guid? lessonKey, string fileName, Stream content,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Guard();
        var buffer = new byte[8192];
        while (await content.ReadAsync(buffer, ct) > 0) { }
        progress?.Report(1);
        Uploads.Add((classCourseId, kind, lessonKey, fileName));
        var dto = new PackageUploadDto(Guid.NewGuid(), kind, PackageUploadStatuses.Uploaded, null, null, null, null, fileName, lessonKey);
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

    public Task<IReadOnlyList<PackageUploadDto>> ListPendingAsync(Guid classCourseId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PackageUploadDto>>(_items.Values.Select(i => i.Dto).ToList());

    public Task<ModuleNodeDto> AttachAsync(Guid id, AttachPackageModuleRequest request, CancellationToken ct = default)
    {
        if (Failure is not null) return Task.FromException<ModuleNodeDto>(Failure);
        if (!_items.TryGetValue(id, out var item))
            return Task.FromException<ModuleNodeDto>(new ApiProblemException(404, ProblemCodes.not_found, null, null));
        if (item.Dto.Status != PackageUploadStatuses.Ready)
            return Task.FromException<ModuleNodeDto>(new ApiProblemException(409, "package_not_ready", null, null));
        Attached.Add(request);
        _items.Remove(id);
        var s = request.Settings;
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
}
