using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;

namespace Lms.Client.Services.LiveSessions;

/// <summary>
/// In-memory live sessions for the standalone dev server and tests. Enforces what matters to the UI: the join window by
/// the fake SERVER clock (<see cref="ServerNow"/>, never the browser's), weekly recurrence (one session and one module per
/// occurrence, at most 52), the This / ThisAndFollowing scopes, the recording link rule, and the provider and feature
/// failure modes (<see cref="ProviderConfigured"/>, <see cref="ProviderHealthy"/>, <see cref="FeatureEnabled"/>, seeded
/// <c>SyncFailed</c> rows). It never returns a URL except from <see cref="JoinAsync"/>. <see cref="AsStudent"/> turns the
/// caller into a student: join window enforced, no authoring, no participants.
/// </summary>
public sealed class FakeLiveSessionsApi : ILiveSessionsApi
{
    /// <summary>The class course the demo data belongs to: the lesson-plan fake's Arabic course, so the page finds its lessons.</summary>
    public static readonly Guid DemoClassCourseId = FakeLessonPlansApi.ArabicClassCourseId;
    private static readonly Guid DemoLessonKey = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private const int JoinWindowMinutes = 10;

    private readonly List<LiveSessionDto> _sessions = [];
    private readonly Dictionary<Guid, List<LiveParticipantDto>> _participants = [];
    private int _created;

    public FakeLiveSessionsApi() : this(seedDemo: true) { }

    public FakeLiveSessionsApi(bool seedDemo)
    {
        if (seedDemo) SeedDemo();
    }

    /// <summary>The fake server clock.</summary>
    public Func<DateTime> ServerNow { get; set; } = () => DateTime.UtcNow;

    public bool AsStudent { get; set; }
    public bool ProviderConfigured { get; set; } = true;
    public bool ProviderHealthy { get; set; } = true;
    public bool FeatureEnabled { get; set; } = true;
    /// <summary>The <c>Retry-After</c> the fake sends with <c>provider_unavailable</c> (the real server always does).</summary>
    public int ProviderRetryAfterSeconds { get; set; } = 300;
    public Exception? Failure { get; set; }
    public List<string> Calls { get; } = [];

    /// <summary>Adds a session without validation (any status, any time); the class course is remembered for the list.</summary>
    public LiveSessionDto Seed(Guid classCourseId, Guid lessonKey, DateTime startsAtUtc, int durationMinutes, string status,
        string title = "Live session", Guid? seriesId = null, int? index = null, int? count = null)
    {
        var settings = new ModuleSettingsInput(title, title, ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null,
            CompletionRules.None, null);
        return Add(classCourseId, lessonKey, settings, startsAtUtc, durationMinutes, status, seriesId, index, count, null);
    }

    private LiveSessionDto Add(Guid classCourseId, Guid lessonKey, ModuleSettingsInput m, DateTime start, int minutes, string status,
        Guid? seriesId, int? index, int? count, LiveRecurrenceDto? recurrence)
    {
        _created++;
        var node = new ModuleNodeDto(Guid.NewGuid(), ModuleKinds.LiveSession, _created, m.TitleAr, m.TitleEn, m.Visibility,
            m.VisibleFromUtc, m.VisibleToUtc, m.Target, m.GroupId, null, null, m.CompletionRule, true, null);
        var session = new LiveSessionDto(Guid.NewGuid(), classCourseId, lessonKey, seriesId, index, count, node, start,
            start.AddMinutes(minutes), minutes, status, recurrence, null, null, start.AddMinutes(-JoinWindowMinutes), !AsStudent, ServerNow());
        _sessions.Add(session);
        return session;
    }

    private LiveSessionDto Find(Guid id)
    {
        var s = _sessions.FirstOrDefault(x => x.Id == id) ?? throw new ApiProblemException(404, ProblemCodes.not_found, null, null);
        return s with { CanEdit = !AsStudent, ServerNowUtc = ServerNow() };
    }

    private void Replace(LiveSessionDto s) => _sessions[_sessions.FindIndex(x => x.Id == s.Id)] = s;

    /// <summary>The attendance session proposed from a live session (M5 wave 3: <c>AttendanceSessionId</c> of the list item), when the demo has one.</summary>
    public Dictionary<Guid, Guid> AttendanceSessions { get; } = [];

    private static ApiProblemException Problem(int status, string code) => new(status, code, null, null);

    private void RequireStaff()
    {
        if (AsStudent) throw Problem(403, "MissingPermission");
    }

    private void Begin(string call)
    {
        Calls.Add(call);
        if (Failure is not null) throw Failure;
    }

    private static void ValidateDuration(int minutes)
    {
        if (minutes is < LiveSessionLimits.DurationMinMinutes or > LiveSessionLimits.DurationMaxMinutes)
            throw Problem(400, ProblemCodes.validation_failed);
    }

    private static void ValidateTitle(ModuleSettingsInput m)
    {
        foreach (var t in new[] { m.TitleAr, m.TitleEn })
            if (string.IsNullOrWhiteSpace(t) || t.Length > LiveSessionLimits.TitleMaxLength) throw Problem(400, ProblemCodes.validation_failed);
    }

    public Task<PagedResult<LiveSessionListItemDto>> ListAsync(Guid classCourseId, LiveSessionQuery query, CancellationToken ct = default)
    {
        Begin($"list {classCourseId}");
        var rows = _sessions.Where(s => s.ClassCourseId == classCourseId
                && (query.FromUtc is null || s.StartsAtUtc >= query.FromUtc)
                && (query.ToUtc is null || s.StartsAtUtc <= query.ToUtc)
                && (string.IsNullOrEmpty(query.Status) || s.Status == query.Status))
            .OrderBy(s => s.StartsAtUtc).ToList();
        var now = ServerNow();
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, Paging.MaxPageSize);
        var items = rows.Skip((page - 1) * size).Take(size)
            .Select(s => new LiveSessionListItemDto(s.Id, s.Module.Id, s.SeriesId, s.OccurrenceIndex, s.Module.TitleAr, s.Module.TitleEn,
                s.StartsAtUtc, s.EndsAtUtc, s.Status, s.RecordingUrl is not null, now,
                AttendanceSessions.TryGetValue(s.Id, out var attendance) ? attendance : null)).ToList();
        return Task.FromResult(new PagedResult<LiveSessionListItemDto>(items, rows.Count, page, size));
    }

    public Task<IReadOnlyList<LiveSessionDto>> CreateAsync(Guid classCourseId, CreateLiveSessionRequest request, CancellationToken ct = default)
    {
        Begin($"create {classCourseId}");
        RequireStaff();
        if (!FeatureEnabled) throw Problem(403, ProblemCodes.feature_disabled);
        if (!ProviderConfigured) throw Problem(409, ProblemCodes.provider_not_configured);
        if (!ProviderHealthy) throw new ApiProblemException(409, ProblemCodes.provider_unavailable, null, null, retryAfterSeconds: ProviderRetryAfterSeconds);
        ValidateTitle(request.Module);
        ValidateDuration(request.DurationMinutes);

        var starts = request.Recurrence is { } r ? RecurrenceExpander.Expand(request.StartsAtUtc, r) : [request.StartsAtUtc];
        var seriesId = request.Recurrence is null ? (Guid?)null : Guid.NewGuid();
        var dto = request.Recurrence is { } rec ? new LiveRecurrenceDto(rec.IntervalWeeks, rec.DaysOfWeek, rec.UntilUtc, rec.Count, rec.TimeZoneId) : null;
        IReadOnlyList<LiveSessionDto> created = starts
            .Select((t, i) => Add(classCourseId, request.LessonKey, request.Module, t, request.DurationMinutes, LiveSessionStatuses.Scheduled,
                seriesId, seriesId is null ? null : i + 1, seriesId is null ? null : starts.Count, dto))
            .ToList();
        return Task.FromResult(created);
    }

    public Task<LiveSessionDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        Begin($"get {id}");
        return Task.FromResult(Find(id));
    }

    public Task<LiveSessionDto> UpdateAsync(Guid id, UpdateLiveSessionRequest request, CancellationToken ct = default)
    {
        Begin($"update {id} {request.Scope}");
        RequireStaff();
        var current = Find(id);
        ValidateTitle(request.Module);
        ValidateDuration(request.DurationMinutes);
        if (!SeriesEditScopes.All.Contains(request.Scope)) throw Problem(400, ProblemCodes.validation_failed);
        var inSeries = current.SeriesId is not null;
        if (request.Scope == SeriesEditScopes.ThisAndFollowing && !inSeries) throw Problem(400, ProblemCodes.validation_failed);
        if (request.Recurrence is not null && !(inSeries && request.Scope == SeriesEditScopes.ThisAndFollowing))
            throw Problem(400, ProblemCodes.validation_failed);

        var delta = request.StartsAtUtc - current.StartsAtUtc;
        var targets = request.Scope == SeriesEditScopes.This
            ? new List<LiveSessionDto> { current }
            : _sessions.Where(s => s.SeriesId == current.SeriesId && s.OccurrenceIndex >= current.OccurrenceIndex).OrderBy(s => s.OccurrenceIndex).ToList();

        if (request.Recurrence is { } rec)
        {
            // Re-create the following occurrences from this one: drop the later rows, then add the new ones after it.
            var starts = RecurrenceExpander.Expand(request.StartsAtUtc, rec);
            _sessions.RemoveAll(s => s.SeriesId == current.SeriesId && s.OccurrenceIndex > current.OccurrenceIndex);
            var total = (current.OccurrenceIndex ?? 1) - 1 + starts.Count;
            var dto = new LiveRecurrenceDto(rec.IntervalWeeks, rec.DaysOfWeek, rec.UntilUtc, rec.Count, rec.TimeZoneId);
            for (var i = 1; i < starts.Count; i++)
                Add(current.ClassCourseId, current.LessonKey, request.Module, starts[i], request.DurationMinutes, LiveSessionStatuses.Scheduled,
                    current.SeriesId, (current.OccurrenceIndex ?? 1) + i, total, dto);
            Apply(current, request, TimeSpan.Zero, request.StartsAtUtc, total, dto);
            RenumberSeries(current.SeriesId, total);
            return Task.FromResult(Find(id));
        }

        foreach (var t in targets) Apply(t, request, delta, t.StartsAtUtc + delta, null, null);
        return Task.FromResult(Find(id));
    }

    private void Apply(LiveSessionDto s, UpdateLiveSessionRequest request, TimeSpan delta, DateTime start, int? count, LiveRecurrenceDto? recurrence)
    {
        var m = request.Module;
        var node = s.Module with
        {
            TitleAr = m.TitleAr, TitleEn = m.TitleEn, Visibility = m.Visibility, VisibleFromUtc = m.VisibleFromUtc, VisibleToUtc = m.VisibleToUtc,
            Target = m.Target, GroupId = m.GroupId, CompletionRule = m.CompletionRule,
        };
        Replace(s with
        {
            Module = node, StartsAtUtc = start, EndsAtUtc = start.AddMinutes(request.DurationMinutes), DurationMinutes = request.DurationMinutes,
            JoinOpensAtUtc = start.AddMinutes(-JoinWindowMinutes), OccurrenceCount = count ?? s.OccurrenceCount, Recurrence = recurrence ?? s.Recurrence,
        });
    }

    private void RenumberSeries(Guid? seriesId, int total)
    {
        for (var i = 0; i < _sessions.Count; i++)
            if (_sessions[i].SeriesId == seriesId) _sessions[i] = _sessions[i] with { OccurrenceCount = total };
    }

    public Task DeleteAsync(Guid id, string scope = SeriesEditScopes.This, CancellationToken ct = default)
    {
        Begin($"delete {id} {scope}");
        RequireStaff();
        var current = Find(id);
        if (!SeriesEditScopes.All.Contains(scope) || (scope == SeriesEditScopes.ThisAndFollowing && current.SeriesId is null))
            throw Problem(400, ProblemCodes.validation_failed);
        var targets = scope == SeriesEditScopes.This
            ? new List<LiveSessionDto> { current }
            : _sessions.Where(s => s.SeriesId == current.SeriesId && s.OccurrenceIndex >= current.OccurrenceIndex).ToList();
        foreach (var t in targets) Replace(t with { Status = LiveSessionStatuses.Cancelled });
        return Task.CompletedTask;
    }

    public Task<LiveSessionJoinDto> JoinAsync(Guid id, CancellationToken ct = default)
    {
        Begin($"join {id}");
        var s = Find(id);
        var now = ServerNow();
        var joinable = s.Status is LiveSessionStatuses.Scheduled or LiveSessionStatuses.Live && now <= s.EndsAtUtc;
        if (!joinable || (AsStudent && now < s.JoinOpensAtUtc)) throw Problem(409, ProblemCodes.live_session_not_open);
        return Task.FromResult(new LiveSessionJoinDto($"https://meet.example.test/j/{s.Id:N}", s.EndsAtUtc, now));
    }

    public Task<LiveSessionDto> SetRecordingAsync(Guid id, RecordingLinkRequest request, CancellationToken ct = default)
    {
        Begin($"recording {id}");
        RequireStaff();
        var s = Find(id);
        if (request.Url is not { Length: > 0 and <= LiveSessionLimits.RecordingUrlMaxLength }
            || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw Problem(400, ProblemCodes.validation_failed);
        Replace(s with { RecordingUrl = request.Url, RecordingSource = RecordingSources.Manual });
        return Task.FromResult(Find(id));
    }

    public Task<PagedResult<LiveParticipantDto>> ListParticipantsAsync(Guid id, LiveParticipantQuery query, CancellationToken ct = default)
    {
        Begin($"participants {id}");
        RequireStaff();
        Find(id);
        var rows = (_participants.GetValueOrDefault(id) ?? [])
            .Where(p => string.IsNullOrEmpty(query.MatchState) || p.MatchState == query.MatchState).OrderBy(p => p.JoinedAtUtc).ToList();
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, Paging.MaxPageSize);
        return Task.FromResult(new PagedResult<LiveParticipantDto>(rows.Skip((page - 1) * size).Take(size).ToList(), rows.Count, page, size));
    }

    public Task<ProviderStatusDto> GetProviderStatusAsync(CancellationToken ct = default)
    {
        Begin("provider-status");
        RequireStaff();
        return Task.FromResult(new ProviderStatusDto(ProviderConfigured, ProviderConfigured && ProviderHealthy));
    }

    public Task<IReadOnlyList<UpcomingLiveSessionDto>> ListUpcomingAsync(CancellationToken ct = default)
    {
        Begin("upcoming");
        var now = ServerNow();
        IReadOnlyList<UpcomingLiveSessionDto> rows = _sessions
            .Where(s => s.EndsAtUtc > now && s.Status is not (LiveSessionStatuses.Ended or LiveSessionStatuses.Cancelled))
            .OrderBy(s => s.StartsAtUtc).Take(LiveSessionLimits.UpcomingMax)
            .Select(s => new UpcomingLiveSessionDto(s.Id, s.Module.Id, s.ClassCourseId, "الرياضيات", "Mathematics", s.Module.TitleAr, s.Module.TitleEn,
                s.StartsAtUtc, s.EndsAtUtc, s.Status, s.JoinOpensAtUtc, now))
            .ToList();
        return Task.FromResult(rows);
    }

    private void SeedDemo()
    {
        var now = ServerNow();
        var cc = DemoClassCourseId;
        var ended = Seed(cc, DemoLessonKey, now.AddDays(-2), 60, LiveSessionStatuses.Ended, "Chapter 3 review");
        Replace(ended with { RecordingUrl = "https://recordings.example.test/chapter-3", RecordingSource = RecordingSources.Manual });
        _participants[ended.Id] =
        [
            Participant("Salma Hassan", ParticipantMatchStates.Matched, ended.StartsAtUtc, 58, "سلمى حسن", "Salma Hassan"),
            Participant("Omar Khaled", ParticipantMatchStates.Matched, ended.StartsAtUtc.AddMinutes(2), 55, "عمر خالد", "Omar Khaled"),
            Participant("iPhone (guest)", ParticipantMatchStates.Guest, ended.StartsAtUtc.AddMinutes(7), 20, null, null),
        ];
        Seed(cc, DemoLessonKey, now.AddMinutes(-20), 60, LiveSessionStatuses.Live, "Problem solving workshop");
        Seed(cc, DemoLessonKey, now.AddMinutes(5), 45, LiveSessionStatuses.Scheduled, "Office hours");
        var series = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
            Seed(cc, DemoLessonKey, now.AddDays(7 * (i + 1)), 60, LiveSessionStatuses.Scheduled, "Weekly revision", series, i + 1, 3);
        Seed(cc, DemoLessonKey, now.AddDays(2), 60, LiveSessionStatuses.SyncFailed, "Exam preparation");
        Seed(cc, DemoLessonKey, now.AddDays(3), 60, LiveSessionStatuses.Cancelled, "Guest lecture");
    }

    private static LiveParticipantDto Participant(string name, string state, DateTime joined, int minutes, string? ar, string? en) =>
        new(Guid.NewGuid(), name, state, state == ParticipantMatchStates.Matched ? Guid.NewGuid() : null, ar, en, joined,
            joined.AddMinutes(minutes), minutes);
}
