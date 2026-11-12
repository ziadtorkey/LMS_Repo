using Lms.Shared;
using Lms.Shared.Content;

namespace Lms.Client.Services.Api;

/// <summary>Lesson modules (FR-CNT-01/02) and their completion/progress (FR-CNT-04). Routes: <c>/api/modules</c> and the class-course lesson routes.</summary>
public interface IModulesApi
{
    /// <summary>GET /api/modules/{id}. A student GET counts as a view.</summary>
    Task<ModuleDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>POST /api/class-courses/{id}/lessons/{lessonKey}/modules (appended last).</summary>
    Task<ModuleDetailDto> CreateAsync(Guid classCourseId, Guid lessonKey, CreateModuleRequest request, CancellationToken ct = default);

    /// <summary>PUT /api/modules/{id}.</summary>
    Task<ModuleDetailDto> UpdateAsync(Guid id, UpdateModuleRequest request, CancellationToken ct = default);

    /// <summary>DELETE /api/modules/{id} (soft delete).</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>PUT /api/class-courses/{id}/lessons/{lessonKey}/modules/order — the lesson's live module ids in the new order.</summary>
    Task ReorderAsync(Guid classCourseId, Guid lessonKey, IReadOnlyList<Guid> moduleIds, CancellationToken ct = default);

    /// <summary>POST /api/modules/{id}/move — to another lesson of the same plan, appended last.</summary>
    Task<ModuleDetailDto> MoveAsync(Guid id, Guid toLessonKey, CancellationToken ct = default);

    /// <summary>GET /api/modules/{id}/completion — the calling student's state.</summary>
    Task<ModuleCompletionDto> GetCompletionAsync(Guid id, CancellationToken ct = default);

    /// <summary>PUT /api/modules/{id}/completion — marks a Manual-rule module complete (idempotent).</summary>
    Task<ModuleCompletionDto> MarkCompleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>GET /api/class-courses/{id}/progress[?studentId=…].</summary>
    Task<CourseProgressDto> GetProgressAsync(Guid classCourseId, Guid? studentId = null, CancellationToken ct = default);
}

public sealed class ModulesApi(ApiHttp http) : IModulesApi
{
    public Task<ModuleDetailDto> GetAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<ModuleDetailDto>(Routes.Module(id), ct);

    public async Task<ModuleDetailDto> CreateAsync(Guid classCourseId, Guid lessonKey, CreateModuleRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ModuleDetailDto>(HttpMethod.Post, Routes.ClassCourseLessonModules(classCourseId, lessonKey), request, ct))!;

    public async Task<ModuleDetailDto> UpdateAsync(Guid id, UpdateModuleRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ModuleDetailDto>(HttpMethod.Put, Routes.Module(id), request, ct))!;

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.Module(id), null, ct);

    public Task ReorderAsync(Guid classCourseId, Guid lessonKey, IReadOnlyList<Guid> moduleIds, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.ClassCourseLessonModulesOrder(classCourseId, lessonKey), new ReorderModulesRequest(moduleIds), ct);

    public async Task<ModuleDetailDto> MoveAsync(Guid id, Guid toLessonKey, CancellationToken ct = default) =>
        (await http.SendAsync<ModuleDetailDto>(HttpMethod.Post, Routes.ModuleMove(id), new MoveModuleRequest(toLessonKey), ct))!;

    public Task<ModuleCompletionDto> GetCompletionAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<ModuleCompletionDto>(Routes.ModuleCompletion(id), ct);

    public async Task<ModuleCompletionDto> MarkCompleteAsync(Guid id, CancellationToken ct = default) =>
        (await http.SendAsync<ModuleCompletionDto>(HttpMethod.Put, Routes.ModuleCompletion(id), null, ct))!;

    public Task<CourseProgressDto> GetProgressAsync(Guid classCourseId, Guid? studentId = null, CancellationToken ct = default) =>
        http.GetAsync<CourseProgressDto>(studentId is { } s ? Routes.ClassCourseProgressOf(classCourseId, s) : Routes.ClassCourseProgress(classCourseId), ct);
}

/// <summary>
/// In-memory modules for the standalone dev server and tests. Enforces the server's content rules that matter to the UI
/// (visibility range, group target, content-kind completion rules, exact reorder set) and keeps modules per
/// (class course, lesson) in order. <see cref="Seed"/> adds modules without validation.
/// </summary>
public sealed class FakeModulesApi : IModulesApi
{
    private readonly List<ModuleDetailDto> _modules = [];
    private int _created;

    /// <summary>The fake server clock.</summary>
    public Func<DateTime> ServerNow { get; set; } = () => DateTime.UtcNow;
    /// <summary>Group ids that exist in a class course (a Group target must be one of them).</summary>
    public Dictionary<Guid, HashSet<Guid>> GroupsByClassCourse { get; } = [];
    public bool CanEdit { get; set; } = true;
    /// <summary>Acts as a student: opening a module (GET) completes a View-rule module, as the server does.</summary>
    public bool AsStudent { get; set; }
    public Exception? Failure { get; set; }
    public List<string> Calls { get; } = [];

    /// <summary>Live modules of a lesson in order, as plan nodes.</summary>
    public IReadOnlyList<ModuleNodeDto> NodesFor(Guid classCourseId, Guid lessonKey) =>
        _modules.Where(m => m.ClassCourseId == classCourseId && m.LessonKey == lessonKey)
            .OrderBy(m => m.Node.Order).Select(m => m.Node).ToList();

    public ModuleDetailDto Seed(Guid classCourseId, Guid lessonKey, CreateModuleRequest request)
    {
        var order = _modules.Count(m => m.ClassCourseId == classCourseId && m.LessonKey == lessonKey) + 1;
        var node = new ModuleNodeDto(Guid.NewGuid(), request.Kind, order, request.TitleAr, request.TitleEn, request.Visibility,
            request.VisibleFromUtc, request.VisibleToUtc, request.Target, request.GroupId, null, null, request.CompletionRule,
            IsVisibleNow(request.Visibility, request.VisibleFromUtc, request.VisibleToUtc), null);
        var detail = new ModuleDetailDto(node, classCourseId, lessonKey, request.CompletionMinScore,
            new ModuleContentDto(null, request.Content.BodyHtmlAr, request.Content.BodyHtmlEn, request.Content.Url, null),
            null, null, CanEdit, ServerNow());
        _modules.Add(detail);
        _created++;
        return detail;
    }

    private bool IsVisibleNow(string visibility, DateTime? from, DateTime? to)
    {
        var now = ServerNow();
        return visibility switch
        {
            ModuleVisibilities.Published => true,
            ModuleVisibilities.Scheduled => (from is null || from <= now) && (to is null || now < to),
            _ => false,
        };
    }

    private static ApiProblemException Problem(string code) => new(400, code, null, null);

    private void Validate(Guid classCourseId, string visibility, DateTime? from, DateTime? to, string target, Guid? groupId, string rule)
    {
        if (visibility == ModuleVisibilities.Scheduled)
        {
            if ((from is null && to is null) || (from is { } f && to is { } t && f >= t)) throw Problem(ProblemCodes.module_visibility_range_invalid);
        }
        else if (from is not null || to is not null) throw Problem(ProblemCodes.module_visibility_range_invalid);

        var groups = GroupsByClassCourse.GetValueOrDefault(classCourseId) ?? [];
        if (target == ModuleTargets.Group ? groupId is not { } g || !groups.Contains(g) : groupId is not null)
            throw Problem(ProblemCodes.module_target_invalid);
        if (!CompletionRules.ForContent.Contains(rule)) throw Problem(ProblemCodes.validation_failed);
    }

    private ModuleDetailDto Find(Guid id) =>
        _modules.FirstOrDefault(m => m.Node.Id == id) ?? throw new ApiProblemException(404, ProblemCodes.not_found, null, null);

    private void Replace(ModuleDetailDto detail) => _modules[_modules.FindIndex(m => m.Node.Id == detail.Node.Id)] = detail;

    public Task<ModuleDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        Calls.Add($"get {id}");
        if (Failure is not null) return Task.FromException<ModuleDetailDto>(Failure);
        var m = Find(id);
        if (AsStudent && m.Node.CompletionRule == CompletionRules.View && m.Node.CompletionState != CompletionStates.Completed)
        {
            m = m with { Node = m.Node with { CompletionState = CompletionStates.Completed } };
            Replace(m);
        }
        return Task.FromResult(m);
    }

    public Task<ModuleDetailDto> CreateAsync(Guid classCourseId, Guid lessonKey, CreateModuleRequest request, CancellationToken ct = default)
    {
        Calls.Add("create");
        if (Failure is not null) return Task.FromException<ModuleDetailDto>(Failure);
        if (!ModuleKinds.Content.Contains(request.Kind)) throw Problem(ProblemCodes.validation_failed);
        Validate(classCourseId, request.Visibility, request.VisibleFromUtc, request.VisibleToUtc, request.Target, request.GroupId, request.CompletionRule);
        return Task.FromResult(Seed(classCourseId, lessonKey, request));
    }

    public Task<ModuleDetailDto> UpdateAsync(Guid id, UpdateModuleRequest request, CancellationToken ct = default)
    {
        Calls.Add($"update {id}");
        if (Failure is not null) return Task.FromException<ModuleDetailDto>(Failure);
        var old = Find(id);
        Validate(old.ClassCourseId, request.Visibility, request.VisibleFromUtc, request.VisibleToUtc, request.Target, request.GroupId, request.CompletionRule);
        var node = old.Node with
        {
            TitleAr = request.TitleAr, TitleEn = request.TitleEn, Visibility = request.Visibility,
            VisibleFromUtc = request.VisibleFromUtc, VisibleToUtc = request.VisibleToUtc, Target = request.Target,
            GroupId = request.GroupId, CompletionRule = request.CompletionRule,
            IsVisibleNow = IsVisibleNow(request.Visibility, request.VisibleFromUtc, request.VisibleToUtc),
        };
        var detail = old with
        {
            Node = node, CompletionMinScore = request.CompletionMinScore,
            Content = new ModuleContentDto(old.Content.Asset, request.Content.BodyHtmlAr, request.Content.BodyHtmlEn, request.Content.Url, old.Content.RefId),
        };
        Replace(detail);
        return Task.FromResult(detail);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        Calls.Add($"delete {id}");
        if (Failure is not null) return Task.FromException(Failure);
        var m = Find(id);
        _modules.Remove(m);
        Renumber(m.ClassCourseId, m.LessonKey);
        return Task.CompletedTask;
    }

    private void Renumber(Guid classCourseId, Guid lessonKey)
    {
        var ordered = _modules.Where(m => m.ClassCourseId == classCourseId && m.LessonKey == lessonKey).OrderBy(m => m.Node.Order).ToList();
        for (var i = 0; i < ordered.Count; i++) Replace(ordered[i] with { Node = ordered[i].Node with { Order = i + 1 } });
    }

    public Task ReorderAsync(Guid classCourseId, Guid lessonKey, IReadOnlyList<Guid> moduleIds, CancellationToken ct = default)
    {
        Calls.Add("reorder");
        if (Failure is not null) return Task.FromException(Failure);
        var current = NodesFor(classCourseId, lessonKey).Select(n => n.Id).ToHashSet();
        if (!current.SetEquals(moduleIds) || moduleIds.Count != current.Count) throw Problem(ProblemCodes.validation_failed);
        for (var i = 0; i < moduleIds.Count; i++)
        {
            var m = Find(moduleIds[i]);
            Replace(m with { Node = m.Node with { Order = i + 1 } });
        }
        return Task.CompletedTask;
    }

    public Task<ModuleDetailDto> MoveAsync(Guid id, Guid toLessonKey, CancellationToken ct = default)
    {
        Calls.Add($"move {id}");
        if (Failure is not null) return Task.FromException<ModuleDetailDto>(Failure);
        var m = Find(id);
        var order = _modules.Count(x => x.ClassCourseId == m.ClassCourseId && x.LessonKey == toLessonKey) + 1;
        var moved = m with { LessonKey = toLessonKey, Node = m.Node with { Order = order } };
        Replace(moved);
        Renumber(m.ClassCourseId, m.LessonKey);
        return Task.FromResult(moved);
    }

    public Task<ModuleCompletionDto> GetCompletionAsync(Guid id, CancellationToken ct = default)
    {
        var m = Find(id);
        return Task.FromResult(new ModuleCompletionDto(id, m.Node.CompletionRule, m.Node.CompletionState ?? CompletionStates.NotStarted, null, null));
    }

    public Task<ModuleCompletionDto> MarkCompleteAsync(Guid id, CancellationToken ct = default)
    {
        var m = Find(id);
        if (m.Node.CompletionRule != CompletionRules.Manual) throw new ApiProblemException(409, ProblemCodes.completion_not_manual, null, null);
        Replace(m with { Node = m.Node with { CompletionState = CompletionStates.Completed } });
        return Task.FromResult(new ModuleCompletionDto(id, m.Node.CompletionRule, CompletionStates.Completed, ServerNow(), null));
    }

    public Task<CourseProgressDto> GetProgressAsync(Guid classCourseId, Guid? studentId = null, CancellationToken ct = default)
    {
        var counted = _modules.Where(m => m.ClassCourseId == classCourseId && m.Node.IsVisibleNow && m.Node.CompletionRule != CompletionRules.None).ToList();
        var done = counted.Count(m => m.Node.CompletionState == CompletionStates.Completed);
        var lessons = counted.GroupBy(m => m.LessonKey)
            .Select(g => new LessonProgressDto(g.Key, new ProgressDto(g.Count(m => m.Node.CompletionState == CompletionStates.Completed), g.Count()))).ToList();
        var modules = counted.Select(m => new ModuleCompletionDto(m.Node.Id, m.Node.CompletionRule, m.Node.CompletionState ?? CompletionStates.NotStarted, null, null)).ToList();
        var next = counted.OrderBy(m => m.Node.Order).FirstOrDefault(m => m.Node.CompletionState != CompletionStates.Completed)?.Node.Id;
        return Task.FromResult(new CourseProgressDto(classCourseId, studentId ?? Guid.Empty, new ProgressDto(done, counted.Count), lessons, modules, next, ServerNow()));
    }
}
