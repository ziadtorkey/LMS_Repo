using Lms.Shared.Blueprints;

namespace Lms.Client.Services.Api;

/// <summary>Versioned course blueprints: published versions, the single draft, publish and copy (FR-LP-01, FR-LP-02, FR-LP-04).</summary>
public interface IBlueprintsApi
{
    Task<IReadOnlyList<BlueprintSummaryDto>> ListVersionsAsync(Guid courseId, CancellationToken ct = default);
    Task<BlueprintDto> GetVersionAsync(Guid courseId, int version, CancellationToken ct = default);

    /// <summary>The course's draft, or null when there is none.</summary>
    Task<BlueprintDto?> GetDraftAsync(Guid courseId, CancellationToken ct = default);

    /// <summary>Creates the draft (a copy of the latest published version, or empty); 409 <c>blueprint_draft_exists</c> when there is one.</summary>
    Task<BlueprintDto> CreateDraftAsync(Guid courseId, CancellationToken ct = default);

    /// <summary>Saves the whole tree; a stale revision answers 409 <c>blueprint_stale</c>.</summary>
    Task<BlueprintDto> SaveDraftAsync(Guid courseId, SaveBlueprintDraftRequest request, CancellationToken ct = default);
    Task<PublishBlueprintResultDto> PublishAsync(Guid courseId, PublishBlueprintRequest request, CancellationToken ct = default);

    /// <summary>Replaces the draft's tree with a copy of another course's (or an older) version, with new keys.</summary>
    Task<BlueprintDto> CopyFromAsync(Guid courseId, CopyBlueprintRequest request, CancellationToken ct = default);
}

public sealed class BlueprintsApi(ApiHttp http) : IBlueprintsApi
{
    private static string Route(Guid courseId) => $"/api/courses/{courseId}/blueprints";

    public async Task<IReadOnlyList<BlueprintSummaryDto>> ListVersionsAsync(Guid courseId, CancellationToken ct = default) =>
        await http.GetAsync<List<BlueprintSummaryDto>>(Route(courseId), ct);

    public Task<BlueprintDto> GetVersionAsync(Guid courseId, int version, CancellationToken ct = default) =>
        http.GetAsync<BlueprintDto>($"{Route(courseId)}/{version}", ct);

    public async Task<BlueprintDto?> GetDraftAsync(Guid courseId, CancellationToken ct = default)
    {
        try
        {
            return await http.GetAsync<BlueprintDto>($"{Route(courseId)}/draft", ct);
        }
        catch (ApiProblemException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<BlueprintDto> CreateDraftAsync(Guid courseId, CancellationToken ct = default) =>
        (await http.SendAsync<BlueprintDto>(HttpMethod.Post, $"{Route(courseId)}/draft", null, ct))!;

    public async Task<BlueprintDto> SaveDraftAsync(Guid courseId, SaveBlueprintDraftRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<BlueprintDto>(HttpMethod.Put, $"{Route(courseId)}/draft", request, ct))!;

    public async Task<PublishBlueprintResultDto> PublishAsync(Guid courseId, PublishBlueprintRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<PublishBlueprintResultDto>(HttpMethod.Post, $"{Route(courseId)}/draft/publish", request, ct))!;

    public async Task<BlueprintDto> CopyFromAsync(Guid courseId, CopyBlueprintRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<BlueprintDto>(HttpMethod.Post, $"{Route(courseId)}/draft/copy-from", request, ct))!;
}
