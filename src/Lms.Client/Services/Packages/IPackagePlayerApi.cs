using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Packages;

namespace Lms.Client.Services.Packages;

/// <summary>Running a package (FR-STD-01, FR-STD-02): launch, staff preview, and the tracking calls the page makes for the frame.</summary>
public interface IPackagePlayerApi
{
    /// <summary>POST /api/packages/{id}/launch — a student's launch (the same call again issues a fresh token for the same attempt).</summary>
    Task<PackageLaunchDto> LaunchAsync(Guid packageId, CancellationToken ct = default);

    /// <summary>POST /api/packages/{id}/preview — staff only; nothing is recorded.</summary>
    Task<PackagePreviewDto> PreviewAsync(Guid packageId, CancellationToken ct = default);

    /// <summary>POST /api/package-attempts/{id}/commit — 204. The page, never the frame, makes this call.</summary>
    Task CommitAsync(Guid attemptId, PackageCommitRequest request, CancellationToken ct = default);

    /// <summary>
    /// GET /api/packages/{id}/my-state — what the viewer needs before launch to pick its button (D57): the grading method, the latest
    /// attempt, whether one is completed and the score the gradebook holds. Students only (staff get 404).
    /// </summary>
    Task<PackageStudentStateDto> GetMyStateAsync(Guid packageId, CancellationToken ct = default);

    /// <summary>POST /api/package-attempts/{id}/finish — flushes and closes the attempt; returns it as the server judged it.</summary>
    Task<PackageAttemptDto> FinishAsync(Guid attemptId, PackageFinishRequest request, CancellationToken ct = default);
}

public sealed class PackagePlayerApi(ApiHttp http) : IPackagePlayerApi
{
    public async Task<PackageLaunchDto> LaunchAsync(Guid packageId, CancellationToken ct = default) =>
        (await http.SendAsync<PackageLaunchDto>(HttpMethod.Post, Routes.PackageLaunch(packageId), null, ct))!;

    public async Task<PackagePreviewDto> PreviewAsync(Guid packageId, CancellationToken ct = default) =>
        (await http.SendAsync<PackagePreviewDto>(HttpMethod.Post, Routes.PackagePreview(packageId), null, ct))!;

    public Task<PackageStudentStateDto> GetMyStateAsync(Guid packageId, CancellationToken ct = default) =>
        http.GetAsync<PackageStudentStateDto>(Routes.PackageMyState(packageId), ct);

    public Task CommitAsync(Guid attemptId, PackageCommitRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.PackageAttemptCommit(attemptId), request, ct);

    public async Task<PackageAttemptDto> FinishAsync(Guid attemptId, PackageFinishRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<PackageAttemptDto>(HttpMethod.Post, Routes.PackageAttemptFinish(attemptId), request, ct))!;
}

/// <summary>
/// In-memory player calls. Launch answers with a content-origin URL and a token that lives <see cref="TokenLifetime"/> by the fake
/// server clock; the attempt id is stable per package (a re-launch keeps the same attempt). Failures are switched on per call.
/// </summary>
public sealed class FakePackagePlayerApi : IPackagePlayerApi
{
    private readonly Dictionary<Guid, Guid> _attempts = [];

    /// <summary>The fake server clock.</summary>
    public Func<DateTime> ServerNow { get; set; } = () => DateTime.UtcNow;
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(120);
    /// <summary>The attempt reported by the launch as resumed (an attempt in progress).</summary>
    public bool Resumed { get; set; }
    public Exception? LaunchFailure { get; set; }
    public Exception? PreviewFailure { get; set; }
    public Exception? CommitFailure { get; set; }
    public Exception? FinishFailure { get; set; }
    /// <summary>The attempt the finish call returns.</summary>
    public PackageAttemptDto? FinishResult { get; set; }
    public List<Guid> Launches { get; } = [];
    public List<Guid> Previews { get; } = [];
    public List<(Guid AttemptId, PackageCommitRequest Request)> Commits { get; } = [];
    public List<(Guid AttemptId, PackageFinishRequest Request)> Finishes { get; } = [];
    /// <summary>Everything the fake did, in order ("launch", "commit 3", "finish 5"): tests check the order of a flush.</summary>
    public List<string> Log { get; } = [];

    public Guid AttemptOf(Guid packageId) =>
        _attempts.TryGetValue(packageId, out var id) ? id : _attempts[packageId] = Guid.NewGuid();

    public Task<PackageLaunchDto> LaunchAsync(Guid packageId, CancellationToken ct = default)
    {
        if (LaunchFailure is not null) return Task.FromException<PackageLaunchDto>(LaunchFailure);
        Launches.Add(packageId);
        Log.Add("launch");
        var now = ServerNow();
        return Task.FromResult(new PackageLaunchDto($"https://content.example.test/p/{Guid.NewGuid():N}/index.html", AttemptOf(packageId),
            now + TokenLifetime, now, Resumed || Launches.Count > 1));
    }

    /// <summary>What the my-state read answers (default: Highest, nothing completed yet).</summary>
    public PackageStudentStateDto MyState { get; set; } = new(PackageGradingMethods.Highest, null, false, null);
    public Exception? MyStateFailure { get; set; }
    public int MyStateReads { get; private set; }

    public Task<PackageStudentStateDto> GetMyStateAsync(Guid packageId, CancellationToken ct = default)
    {
        MyStateReads++;
        return MyStateFailure is not null ? Task.FromException<PackageStudentStateDto>(MyStateFailure) : Task.FromResult(MyState);
    }

    public Task<PackagePreviewDto> PreviewAsync(Guid packageId, CancellationToken ct = default)
    {
        if (PreviewFailure is not null) return Task.FromException<PackagePreviewDto>(PreviewFailure);
        Previews.Add(packageId);
        var now = ServerNow();
        return Task.FromResult(new PackagePreviewDto($"https://content.example.test/p/{Guid.NewGuid():N}/index.html", now + TokenLifetime, now));
    }

    public Task CommitAsync(Guid attemptId, PackageCommitRequest request, CancellationToken ct = default)
    {
        if (CommitFailure is not null) return Task.FromException(CommitFailure);
        Commits.Add((attemptId, request));
        Log.Add($"commit {request.Seq}");
        return Task.CompletedTask;
    }

    public Task<PackageAttemptDto> FinishAsync(Guid attemptId, PackageFinishRequest request, CancellationToken ct = default)
    {
        if (FinishFailure is not null) return Task.FromException<PackageAttemptDto>(FinishFailure);
        Finishes.Add((attemptId, request));
        Log.Add($"finish {request.LastSeq}");
        return Task.FromResult(FinishResult ?? new PackageAttemptDto(attemptId, "Completed", true, 85, 0, 100, request.LastSeq, ServerNow()));
    }
}
