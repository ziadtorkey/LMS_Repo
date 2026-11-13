using Lms.Shared.Blueprints;

namespace Lms.Client.Services.Api;

/// <summary>In-memory blueprints for the standalone client and bUnit tests; records what the UI sent.</summary>
public sealed class FakeBlueprintsApi : IBlueprintsApi
{
    private readonly Dictionary<Guid, List<BlueprintDto>> _published = [];
    private readonly Dictionary<Guid, BlueprintDto> _drafts = [];

    public SaveBlueprintDraftRequest? LastSave { get; private set; }
    public PublishBlueprintRequest? LastPublish { get; private set; }
    public CopyBlueprintRequest? LastCopy { get; private set; }
    public int CreatedDrafts { get; private set; }

    /// <summary>The standalone dev client seeds a first published version for every course it is asked about.</summary>
    public bool AutoSeed { get; init; }

    private readonly HashSet<Guid> _seen = [];

    private void Seed(Guid courseId)
    {
        if (AutoSeed && _seen.Add(courseId) && !_published.ContainsKey(courseId)) SeedPublished(courseId);
    }

    /// <summary>When set, the next save throws it (tests simulate blueprint_stale and other problems).</summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>How many class plans the next publish reports as queued.</summary>
    public int ClassesQueued { get; set; } = 4;

    /// <summary>Seeds version 1 of a course: two sections with two lessons each.</summary>
    public BlueprintDto SeedPublished(Guid courseId, int version = 1)
    {
        var sections = Enumerable.Range(1, 2).Select(s => new BlueprintSectionDto(Guid.NewGuid(), s, $"الأسبوع {s}", $"Week {s}",
            Enumerable.Range(1, 2).Select(l => new BlueprintLessonDto(Guid.NewGuid(), l, $"الدرس {s}.{l}", $"Lesson {s}.{l}", null, null, (s - 1) * 7 + l)).ToList())).ToList();
        var summary = new BlueprintSummaryDto(Guid.NewGuid(), courseId, version, BlueprintStatuses.Published, 1, DateTime.UtcNow.AddDays(-3),
            "المشرف", "The Supervisor", BlueprintApplyModes.NewClassesOnly, 2, 4);
        var dto = new BlueprintDto(summary, sections);
        if (!_published.TryGetValue(courseId, out var list)) _published[courseId] = list = [];
        list.Add(dto);
        return dto;
    }

    public BlueprintDto? DraftOf(Guid courseId) => _drafts.GetValueOrDefault(courseId);

    public Task<IReadOnlyList<BlueprintSummaryDto>> ListVersionsAsync(Guid courseId, CancellationToken ct = default)
    {
        Seed(courseId);
        var rows = _published.GetValueOrDefault(courseId, []).Select(b => b.Summary).ToList();
        if (_drafts.TryGetValue(courseId, out var draft)) rows.Add(draft.Summary);
        return Task.FromResult<IReadOnlyList<BlueprintSummaryDto>>(rows.OrderByDescending(r => r.Version == 0 ? int.MaxValue : r.Version).ToList());
    }

    public Task<BlueprintDto> GetVersionAsync(Guid courseId, int version, CancellationToken ct = default) =>
        _published.GetValueOrDefault(courseId, []).FirstOrDefault(b => b.Summary.Version == version) is { } found
            ? Task.FromResult(found)
            : Task.FromException<BlueprintDto>(new ApiProblemException(404, "not_found", null, null));

    public Task<BlueprintDto?> GetDraftAsync(Guid courseId, CancellationToken ct = default) =>
        Task.FromResult(_drafts.GetValueOrDefault(courseId));

    public Task<BlueprintDto> CreateDraftAsync(Guid courseId, CancellationToken ct = default)
    {
        if (_drafts.ContainsKey(courseId))
            return Task.FromException<BlueprintDto>(new ApiProblemException(409, "blueprint_draft_exists", null, null));
        CreatedDrafts++;
        var latest = _published.GetValueOrDefault(courseId, []).LastOrDefault();
        var summary = new BlueprintSummaryDto(Guid.NewGuid(), courseId, 0, BlueprintStatuses.Draft, 1, null, null, null, null,
            latest?.Summary.SectionCount ?? 0, latest?.Summary.LessonCount ?? 0);
        var draft = new BlueprintDto(summary, latest?.Sections ?? []);
        _drafts[courseId] = draft;
        return Task.FromResult(draft);
    }

    public Task<BlueprintDto> SaveDraftAsync(Guid courseId, SaveBlueprintDraftRequest request, CancellationToken ct = default)
    {
        if (SaveFailure is { } failure)
        {
            SaveFailure = null;
            return Task.FromException<BlueprintDto>(failure);
        }
        LastSave = request;
        var draft = _drafts[courseId];
        if (draft.Summary.Revision != request.Revision)
            return Task.FromException<BlueprintDto>(new ApiProblemException(409, "blueprint_stale", null, null));
        var sections = request.Sections.Select((s, i) => new BlueprintSectionDto(s.Key ?? Guid.NewGuid(), i + 1, s.TitleAr, s.TitleEn,
            s.Lessons.Select((l, j) => new BlueprintLessonDto(l.Key ?? Guid.NewGuid(), j + 1, l.TitleAr, l.TitleEn, l.ObjectivesAr, l.ObjectivesEn, l.PlannedOffsetDays)).ToList())).ToList();
        var saved = new BlueprintDto(draft.Summary with
        {
            Revision = draft.Summary.Revision + 1, SectionCount = sections.Count, LessonCount = sections.Sum(s => s.Lessons.Count),
        }, sections);
        _drafts[courseId] = saved;
        return Task.FromResult(saved);
    }

    public Task<PublishBlueprintResultDto> PublishAsync(Guid courseId, PublishBlueprintRequest request, CancellationToken ct = default)
    {
        LastPublish = request;
        var draft = _drafts[courseId];
        if (draft.Sections.Count == 0 || draft.Sections.Any(s => s.Lessons.Count == 0))
            return Task.FromException<PublishBlueprintResultDto>(new ApiProblemException(400, "blueprint_empty", null, null));
        var version = _published.GetValueOrDefault(courseId, []).Count + 1;
        var summary = draft.Summary with { Version = version, Status = BlueprintStatuses.Published, PublishedAt = DateTime.UtcNow, ApplyMode = request.ApplyMode };
        if (!_published.TryGetValue(courseId, out var list)) _published[courseId] = list = [];
        for (var i = 0; i < list.Count; i++) list[i] = list[i] with { Summary = list[i].Summary with { Status = BlueprintStatuses.Superseded } };
        list.Add(draft with { Summary = summary });
        _drafts.Remove(courseId);
        return Task.FromResult(new PublishBlueprintResultDto(summary, request.ApplyMode == BlueprintApplyModes.PushToExisting ? ClassesQueued : 0));
    }

    public Task<BlueprintDto> CopyFromAsync(Guid courseId, CopyBlueprintRequest request, CancellationToken ct = default)
    {
        LastCopy = request;
        var source = _published.GetValueOrDefault(request.SourceCourseId, [])
            .LastOrDefault(b => request.SourceVersion is null || b.Summary.Version == request.SourceVersion);
        if (source is null) return Task.FromException<BlueprintDto>(new ApiProblemException(404, "not_found", null, null));
        var draft = _drafts[courseId];
        var sections = source.Sections.Select(s => s with
        {
            Key = Guid.NewGuid(), Lessons = s.Lessons.Select(l => l with { Key = Guid.NewGuid() }).ToList(),
        }).ToList();
        var copied = new BlueprintDto(draft.Summary with { Revision = draft.Summary.Revision + 1, SectionCount = sections.Count }, sections);
        _drafts[courseId] = copied;
        return Task.FromResult(copied);
    }
}
