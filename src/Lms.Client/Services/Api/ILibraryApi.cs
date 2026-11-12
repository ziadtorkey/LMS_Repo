using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.Library;

namespace Lms.Client.Services.Api;

/// <summary>The digital library of a unit (FR-LIB-01): search, manage, signed download and add to a lesson. Routes: <c>/api/library</c>.</summary>
public interface ILibraryApi
{
    Task<PagedResult<LibraryResourceDto>> SearchAsync(LibraryQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<string>> TagsAsync(Guid orgUnitId, CancellationToken ct = default);
    Task<LibraryResourceDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<LibraryResourceDto> CreateAsync(SaveLibraryResourceRequest request, CancellationToken ct = default);
    Task<LibraryResourceDto> UpdateAsync(Guid id, SaveLibraryResourceRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>GET /api/library/{id}/download — a signed URL; 403 <c>download_not_allowed</c> when the resource is reader-only.</summary>
    Task<AssetUrlDto> DownloadAsync(Guid id, CancellationToken ct = default);

    /// <summary>POST /api/library/{id}/add-to-lesson — the new <c>LibraryResource</c> module.</summary>
    Task<ModuleNodeDto> AddToLessonAsync(Guid id, AddLibraryResourceToLessonRequest request, CancellationToken ct = default);
}

public sealed class LibraryApi(ApiHttp http) : ILibraryApi
{
    public Task<PagedResult<LibraryResourceDto>> SearchAsync(LibraryQuery q, CancellationToken ct = default)
    {
        var parts = new List<string>();
        void Add(string name, object? value) { if (value is not null && value.ToString() is { Length: > 0 } s) parts.Add($"{name}={Uri.EscapeDataString(s)}"); }
        Add("orgUnitId", q.OrgUnitId); Add("search", q.Search); Add("type", q.Type); Add("levelTypeId", q.LevelTypeId);
        Add("courseId", q.CourseId); Add("tag", q.Tag); Add("page", q.Page); Add("pageSize", q.PageSize);
        return http.GetAsync<PagedResult<LibraryResourceDto>>(Routes.Library + (parts.Count == 0 ? "" : "?" + string.Join("&", parts)), ct);
    }

    public async Task<IReadOnlyList<string>> TagsAsync(Guid orgUnitId, CancellationToken ct = default) =>
        await http.GetAsync<List<string>>(Routes.LibraryTagsOf(orgUnitId), ct);

    public Task<LibraryResourceDto> GetAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<LibraryResourceDto>(Routes.LibraryResource(id), ct);

    public async Task<LibraryResourceDto> CreateAsync(SaveLibraryResourceRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<LibraryResourceDto>(HttpMethod.Post, Routes.Library, request, ct))!;

    public async Task<LibraryResourceDto> UpdateAsync(Guid id, SaveLibraryResourceRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<LibraryResourceDto>(HttpMethod.Put, Routes.LibraryResource(id), request, ct))!;

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.LibraryResource(id), null, ct);

    public Task<AssetUrlDto> DownloadAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<AssetUrlDto>(Routes.LibraryResourceDownload(id), ct);

    public async Task<ModuleNodeDto> AddToLessonAsync(Guid id, AddLibraryResourceToLessonRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ModuleNodeDto>(HttpMethod.Post, Routes.LibraryResourceAddToLesson(id), request, ct))!;
}

/// <summary>In-memory library: filters compose like the server's (AND), tags are whole-tag matches.</summary>
public sealed class FakeLibraryApi : ILibraryApi
{
    public static readonly Guid UnitId = FakeUnits.SchoolId;

    public List<LibraryResourceDto> Resources { get; } = [];
    public List<(Guid ResourceId, AddLibraryResourceToLessonRequest Request)> Added { get; } = [];
    public List<LibraryQuery> Queries { get; } = [];
    public Exception? Failure { get; set; }

    public LibraryResourceDto Add(string title, string type = LibraryResourceTypes.Document, IReadOnlyList<Guid>? levels = null, Guid? courseId = null,
        IReadOnlyList<string>? tags = null, bool downloadAllowed = true, bool canEdit = true, string? url = null, AssetDto? file = null)
    {
        var r = new LibraryResourceDto(Guid.NewGuid(), UnitId, "المدرسة الرئيسية", "Main School", false, title + " ar", title, null, null, type,
            levels ?? [], courseId, null, null, tags ?? [], null, file, url, downloadAllowed, canEdit, DateTime.UtcNow);
        Resources.Add(r);
        return r;
    }

    public Task<PagedResult<LibraryResourceDto>> SearchAsync(LibraryQuery q, CancellationToken ct = default)
    {
        Queries.Add(q);
        if (Failure is not null) return Task.FromException<PagedResult<LibraryResourceDto>>(Failure);
        var items = Resources
            .Where(r => q.Search is not { Length: > 0 } s || r.TitleEn.Contains(s, StringComparison.OrdinalIgnoreCase) || r.TitleAr.Contains(s))
            .Where(r => q.Type is null || r.Type == q.Type)
            .Where(r => q.LevelTypeId is not { } l || r.LevelTypeIds.Count == 0 || r.LevelTypeIds.Contains(l))
            .Where(r => q.CourseId is null || r.CourseId == q.CourseId)
            .Where(r => q.Tag is null || r.Tags.Contains(q.Tag, StringComparer.OrdinalIgnoreCase))
            .OrderBy(r => r.TitleEn).ToList();
        return Task.FromResult(new PagedResult<LibraryResourceDto>(items, items.Count, q.Page, q.PageSize));
    }

    public Task<IReadOnlyList<string>> TagsAsync(Guid orgUnitId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Resources.SelectMany(r => r.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList());

    public Task<LibraryResourceDto> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Resources.FirstOrDefault(r => r.Id == id) ?? throw new ApiProblemException(404, ProblemCodes.not_found, null, null));

    public Task<LibraryResourceDto> CreateAsync(SaveLibraryResourceRequest request, CancellationToken ct = default)
    {
        if (Failure is not null) return Task.FromException<LibraryResourceDto>(Failure);
        var created = Add(request.TitleEn, request.Type, request.LevelTypeIds, request.CourseId, request.Tags, request.DownloadAllowed, url: request.Url);
        var withAr = created with { TitleAr = request.TitleAr, DescriptionAr = request.DescriptionAr, DescriptionEn = request.DescriptionEn };
        Resources[Resources.Count - 1] = withAr;
        return Task.FromResult(withAr);
    }

    public Task<LibraryResourceDto> UpdateAsync(Guid id, SaveLibraryResourceRequest request, CancellationToken ct = default)
    {
        if (Failure is not null) return Task.FromException<LibraryResourceDto>(Failure);
        var i = Resources.FindIndex(r => r.Id == id);
        if (i < 0) throw new ApiProblemException(404, ProblemCodes.not_found, null, null);
        Resources[i] = Resources[i] with
        {
            TitleAr = request.TitleAr, TitleEn = request.TitleEn, DescriptionAr = request.DescriptionAr, DescriptionEn = request.DescriptionEn,
            Type = request.Type, LevelTypeIds = request.LevelTypeIds, CourseId = request.CourseId, Tags = request.Tags,
            Url = request.Url, DownloadAllowed = request.DownloadAllowed,
        };
        return Task.FromResult(Resources[i]);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        Resources.RemoveAll(r => r.Id == id);
        return Task.CompletedTask;
    }

    public Task<AssetUrlDto> DownloadAsync(Guid id, CancellationToken ct = default)
    {
        var r = Resources.FirstOrDefault(x => x.Id == id) ?? throw new ApiProblemException(404, ProblemCodes.not_found, null, null);
        if (!r.DownloadAllowed) throw new ApiProblemException(403, "download_not_allowed", null, null);
        var now = DateTime.UtcNow;
        return Task.FromResult(new AssetUrlDto($"https://files.test/library/{id}", now.AddMinutes(10), now));
    }

    public Task<ModuleNodeDto> AddToLessonAsync(Guid id, AddLibraryResourceToLessonRequest request, CancellationToken ct = default)
    {
        if (Failure is not null) return Task.FromException<ModuleNodeDto>(Failure);
        Added.Add((id, request));
        var m = request.Module;
        return Task.FromResult(new ModuleNodeDto(Guid.NewGuid(), ModuleKinds.LibraryResource, 1, m.TitleAr, m.TitleEn, m.Visibility,
            m.VisibleFromUtc, m.VisibleToUtc, m.Target, m.GroupId, null, null, m.CompletionRule, true, null));
    }
}
