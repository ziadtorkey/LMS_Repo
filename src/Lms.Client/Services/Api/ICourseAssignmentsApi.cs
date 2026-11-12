using Lms.Shared;
using Lms.Shared.Assignments;
using Lms.Shared.Content;

namespace Lms.Client.Services.Api;

/// <summary>Assignments of a class course, their submissions and grading (FR-ASG-01, FR-ASG-02). Teacher side.</summary>
public interface ICourseAssignmentsApi
{
    Task<PagedResult<CourseAssignmentListItemDto>> ListAsync(Guid classCourseId, int page, int pageSize, CancellationToken ct = default);
    Task<CourseAssignmentDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<CourseAssignmentDto> CreateAsync(Guid classCourseId, CreateCourseAssignmentRequest request, CancellationToken ct = default);
    Task<CourseAssignmentDto> UpdateAsync(Guid id, UpdateCourseAssignmentRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<SubmissionListItemDto>> ListSubmissionsAsync(Guid assignmentId, SubmissionQuery query, CancellationToken ct = default);
    Task<SubmissionDto> GetSubmissionAsync(Guid submissionId, CancellationToken ct = default);

    /// <summary>Saves grade and feedback (status Graded); the student sees nothing until <see cref="ReturnAsync"/>.</summary>
    Task<SubmissionDto> GradeAsync(Guid submissionId, GradeSubmissionRequest request, CancellationToken ct = default);

    /// <summary>Releases grade and feedback to the student (status Returned).</summary>
    Task<SubmissionDto> ReturnAsync(Guid submissionId, CancellationToken ct = default);

    // ---- student side (M4a T21, FE2) ----

    /// <summary>GET /api/course-assignments/{id}/my-submission — the assignment, the caller's live submission and whether they may submit now.</summary>
    Task<MySubmissionDto> GetMySubmissionAsync(Guid assignmentId, CancellationToken ct = default);

    /// <summary>POST /api/course-assignments/{id}/submit — a hand-in, a resubmission, or a draft.</summary>
    Task<SubmissionDto> SubmitAsync(Guid assignmentId, SubmitAssignmentRequest request, CancellationToken ct = default);
}

public sealed class CourseAssignmentsApi(ApiHttp http) : ICourseAssignmentsApi
{
    public Task<PagedResult<CourseAssignmentListItemDto>> ListAsync(Guid classCourseId, int page, int pageSize, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<CourseAssignmentListItemDto>>($"{Routes.ClassCourseAssignments(classCourseId)}?page={page}&pageSize={pageSize}", ct);

    public Task<CourseAssignmentDto> GetAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<CourseAssignmentDto>(Routes.CourseAssignment(id), ct);

    public async Task<CourseAssignmentDto> CreateAsync(Guid classCourseId, CreateCourseAssignmentRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<CourseAssignmentDto>(HttpMethod.Post, Routes.ClassCourseAssignments(classCourseId), request, ct))!;

    public async Task<CourseAssignmentDto> UpdateAsync(Guid id, UpdateCourseAssignmentRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<CourseAssignmentDto>(HttpMethod.Put, Routes.CourseAssignment(id), request, ct))!;

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.CourseAssignment(id), null, ct);

    public Task<PagedResult<SubmissionListItemDto>> ListSubmissionsAsync(Guid assignmentId, SubmissionQuery query, CancellationToken ct = default)
    {
        var parts = new List<string> { $"page={query.Page}", $"pageSize={query.PageSize}" };
        if (!string.IsNullOrWhiteSpace(query.Status)) parts.Add($"status={Uri.EscapeDataString(query.Status)}");
        if (query.IsLate is { } late) parts.Add($"isLate={late.ToString().ToLowerInvariant()}");
        if (!string.IsNullOrWhiteSpace(query.Search)) parts.Add($"search={Uri.EscapeDataString(query.Search)}");
        return http.GetAsync<PagedResult<SubmissionListItemDto>>($"{Routes.CourseAssignmentSubmissions(assignmentId)}?{string.Join('&', parts)}", ct);
    }

    public Task<SubmissionDto> GetSubmissionAsync(Guid submissionId, CancellationToken ct = default) =>
        http.GetAsync<SubmissionDto>(Routes.Submission(submissionId), ct);

    public async Task<SubmissionDto> GradeAsync(Guid submissionId, GradeSubmissionRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<SubmissionDto>(HttpMethod.Put, Routes.SubmissionGrade(submissionId), request, ct))!;

    public async Task<SubmissionDto> ReturnAsync(Guid submissionId, CancellationToken ct = default) =>
        (await http.SendAsync<SubmissionDto>(HttpMethod.Post, Routes.SubmissionReturn(submissionId), null, ct))!;

    // ---- student side (M4a T21, FE2) ----

    public Task<MySubmissionDto> GetMySubmissionAsync(Guid assignmentId, CancellationToken ct = default) =>
        http.GetAsync<MySubmissionDto>(Routes.CourseAssignmentMySubmission(assignmentId), ct);

    public async Task<SubmissionDto> SubmitAsync(Guid assignmentId, SubmitAssignmentRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<SubmissionDto>(HttpMethod.Post, Routes.CourseAssignmentSubmit(assignmentId), request, ct))!;
}

/// <summary>
/// In-memory assignments for the standalone dev server and tests. Applies the server rules the UI depends on: late-policy
/// shape (400 late_policy_invalid), grade 0..MaxGrade (400 validation_failed), the penalty at grading, and release on return.
/// </summary>
public sealed class FakeCourseAssignmentsApi : ICourseAssignmentsApi
{
    public static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000a5501");
    public static readonly Guid OnTimeSubmissionId = Guid.Parse("00000000-0000-0000-0000-0000000a5511");
    public static readonly Guid LateSubmissionId = Guid.Parse("00000000-0000-0000-0000-0000000a5512");
    public static readonly Guid GradedSubmissionId = Guid.Parse("00000000-0000-0000-0000-0000000a5513");

    public Dictionary<Guid, CourseAssignmentDto> Assignments { get; } = [];
    public Dictionary<Guid, SubmissionDto> Submissions { get; } = [];
    /// <summary>Roster rows (students without a hand-in have a null SubmissionId).</summary>
    public List<(Guid AssignmentId, SubmissionListItemDto Row)> Rows { get; } = [];
    public List<string> Calls { get; } = [];
    public List<UpdateCourseAssignmentRequest> Updates { get; } = [];
    public List<(Guid SubmissionId, GradeSubmissionRequest Request)> GradeRequests { get; } = [];
    /// <summary>Assets a test uploaded, so a grade can return the annotated file's real name.</summary>
    public Dictionary<Guid, AssetDto> KnownAssets { get; } = [];
    public Exception? Failure { get; set; }

    public FakeCourseAssignmentsApi()
    {
        var classCourse = FakeLessonPlansApi.ArabicClassCourseId;
        var due = new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc);
        var module = new ModuleNodeDto(Guid.Parse("00000000-0000-0000-0000-0000000a5500"), ModuleKinds.Assignment, 1, "واجب القراءة", "Reading homework",
            ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, null, null, CompletionRules.Submit, true, null);
        Assignments[AssignmentId] = new CourseAssignmentDto(AssignmentId, classCourse, Guid.Parse("00000000-0000-0000-0000-0000000000b1"), module,
            "<p>اقرأ الدرس</p>", "<p>Read the lesson</p>", [], due, 20m, LatePolicies.Penalty, 10, ResubmissionRules.UntilDue, true, true,
            3, 1, true, DateTime.UtcNow);
        AddSubmission(OnTimeSubmissionId, "سارة", "Sara", SubmissionStatuses.Submitted, false, due.AddHours(-3), null);
        AddSubmission(LateSubmissionId, "عمر", "Omar", SubmissionStatuses.Late, true, due.AddHours(5), null);
        AddSubmission(GradedSubmissionId, "ليلى", "Laila", SubmissionStatuses.Graded, false, due.AddHours(-30),
            new SubmissionGradeDto(18m, null, 18m, null, null, due, null));
        Rows.Add((AssignmentId, new SubmissionListItemDto(Guid.NewGuid(), "خالد", "Khalid", null, null, null, false, null)));
    }

    private void AddSubmission(Guid id, string nameAr, string nameEn, string status, bool late, DateTime at, SubmissionGradeDto? grade)
    {
        var student = Guid.NewGuid();
        Submissions[id] = new SubmissionDto(id, AssignmentId, student, nameAr, nameEn, status, "<p>My answer</p>", [], at, late, 1, grade);
        Rows.Add((AssignmentId, new SubmissionListItemDto(student, nameAr, nameEn, id, status, at, late, grade?.FinalGrade)));
    }

    private Task<T> Run<T>(string call, Func<T> action)
    {
        Calls.Add(call);
        if (Failure is not null) return Task.FromException<T>(Failure);
        try { return Task.FromResult(action()); }
        catch (ApiProblemException ex) { return Task.FromException<T>(ex); }
    }

    private static ApiProblemException Problem(string code) => new(400, code, null, null);
    private static ApiProblemException NotFound() => new(404, ProblemCodes.not_found, null, null);

    private static void Validate(decimal maxGrade, string latePolicy, int? penalty, DateTime? due, bool allowText, bool allowFiles)
    {
        if (maxGrade is < 0.01m or > 1000m || (!allowText && !allowFiles)) throw Problem(ProblemCodes.validation_failed);
        if (latePolicy == LatePolicies.Penalty ? penalty is not (>= 1 and <= 100) : penalty is not null) throw Problem(ProblemCodes.late_policy_invalid);
        if (latePolicy == LatePolicies.Block && due is null) throw Problem(ProblemCodes.late_policy_invalid);
    }

    private static ModuleNodeDto NodeFor(ModuleSettingsInput m, Guid id, Guid? existing = null) =>
        new(existing ?? Guid.NewGuid(), ModuleKinds.Assignment, 1, m.TitleAr, m.TitleEn, m.Visibility, m.VisibleFromUtc, m.VisibleToUtc, m.Target,
            m.GroupId, null, null, m.CompletionRule, m.Visibility == ModuleVisibilities.Published, null);

    public Task<PagedResult<CourseAssignmentListItemDto>> ListAsync(Guid classCourseId, int page, int pageSize, CancellationToken ct = default) =>
        Run("list", () =>
        {
            var items = Assignments.Values.Where(a => a.ClassCourseId == classCourseId).OrderBy(a => a.DueAtUtc)
                .Select(a => new CourseAssignmentListItemDto(a.Id, a.Module.Id, a.LessonKey, a.Module.TitleAr, a.Module.TitleEn, a.Module.Visibility,
                    a.Module.IsVisibleNow, a.DueAtUtc, a.MaxGrade, a.SubmittedCount, a.GradedCount, null, null)).ToList();
            return new PagedResult<CourseAssignmentListItemDto>(items.Skip((page - 1) * pageSize).Take(pageSize).ToList(), items.Count, page, pageSize);
        });

    public Task<CourseAssignmentDto> GetAsync(Guid id, CancellationToken ct = default) =>
        Run("get", () => Assignments.TryGetValue(id, out var a) ? a : throw NotFound());

    public Task<CourseAssignmentDto> CreateAsync(Guid classCourseId, CreateCourseAssignmentRequest r, CancellationToken ct = default) =>
        Run("create", () =>
        {
            Validate(r.MaxGrade, r.LatePolicy, r.PenaltyPercent, r.DueAtUtc, r.AllowText, r.AllowFiles);
            var id = Guid.NewGuid();
            var created = new CourseAssignmentDto(id, classCourseId, r.LessonKey, NodeFor(r.Module, id), r.InstructionsHtmlAr, r.InstructionsHtmlEn, [],
                r.DueAtUtc, r.MaxGrade, r.LatePolicy, r.PenaltyPercent, r.ResubmissionRule, r.AllowText, r.AllowFiles, 0, 0, true, DateTime.UtcNow);
            return Assignments[id] = created;
        });

    public Task<CourseAssignmentDto> UpdateAsync(Guid id, UpdateCourseAssignmentRequest r, CancellationToken ct = default) =>
        Run("update", () =>
        {
            Updates.Add(r);
            if (!Assignments.TryGetValue(id, out var a)) throw NotFound();
            Validate(r.MaxGrade, r.LatePolicy, r.PenaltyPercent, r.DueAtUtc, r.AllowText, r.AllowFiles);
            return Assignments[id] = a with
            {
                Module = NodeFor(r.Module, id, a.Module.Id), InstructionsHtmlAr = r.InstructionsHtmlAr, InstructionsHtmlEn = r.InstructionsHtmlEn,
                DueAtUtc = r.DueAtUtc, MaxGrade = r.MaxGrade, LatePolicy = r.LatePolicy, PenaltyPercent = r.PenaltyPercent,
                ResubmissionRule = r.ResubmissionRule, AllowText = r.AllowText, AllowFiles = r.AllowFiles,
            };
        });

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        Run("delete", () => Assignments.Remove(id) ? true : throw NotFound());

    public Task<PagedResult<SubmissionListItemDto>> ListSubmissionsAsync(Guid assignmentId, SubmissionQuery q, CancellationToken ct = default) =>
        Run("submissions", () =>
        {
            var rows = Rows.Where(r => r.AssignmentId == assignmentId).Select(r => r.Row)
                .Where(r => q.Status is null || (q.Status == "None" ? r.Status is null : r.Status == q.Status))
                .Where(r => q.IsLate is null || r.IsLate == q.IsLate)
                .Where(r => string.IsNullOrWhiteSpace(q.Search) || r.StudentNameAr.Contains(q.Search) || r.StudentNameEn.Contains(q.Search, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return new PagedResult<SubmissionListItemDto>(rows.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList(), rows.Count, q.Page, q.PageSize);
        });

    public Task<SubmissionDto> GetSubmissionAsync(Guid submissionId, CancellationToken ct = default) =>
        Run("submission", () => Submissions.TryGetValue(submissionId, out var s) ? s : throw NotFound());

    public Task<SubmissionDto> GradeAsync(Guid submissionId, GradeSubmissionRequest r, CancellationToken ct = default) =>
        Run("grade", () =>
        {
            GradeRequests.Add((submissionId, r));
            if (!Submissions.TryGetValue(submissionId, out var s)) throw NotFound();
            var a = Assignments[s.AssignmentId];
            if (r.Grade < 0 || r.Grade > a.MaxGrade) throw Problem(ProblemCodes.validation_failed);
            int? penalty = s.IsLate && a.LatePolicy == LatePolicies.Penalty ? a.PenaltyPercent : null;
            var final = penalty is { } p ? Math.Round(r.Grade * (100 - p) / 100m, 2) : r.Grade;
            // the annotated file stays on the grade (the real server returns its asset)
            var annotated = r.AnnotatedFileAssetId is { } fileId
                ? KnownAssets.GetValueOrDefault(fileId) ?? new AssetDto(fileId, a.ClassCourseId, AssetKinds.Document, "annotated.pdf", "application/pdf", 0, null, false, DateTime.UtcNow)
                : null;
            var grade = new SubmissionGradeDto(r.Grade, penalty, final, r.FeedbackHtml, annotated, DateTime.UtcNow, s.Grade?.ReturnedAtUtc);
            return Submissions[submissionId] = s with { Status = s.Status == SubmissionStatuses.Returned ? s.Status : SubmissionStatuses.Graded, Grade = grade };
        });

    public Task<SubmissionDto> ReturnAsync(Guid submissionId, CancellationToken ct = default) =>
        Run("return", () =>
        {
            if (!Submissions.TryGetValue(submissionId, out var s) || s.Grade is null) throw NotFound();
            return Submissions[submissionId] = s with { Status = SubmissionStatuses.Returned, Grade = s.Grade with { ReturnedAtUtc = DateTime.UtcNow } };
        });

    // ---- student side (M4a T21, FE2) ----

    /// <summary>The fake server clock (deadlines are judged by it, never by the browser).</summary>
    public Func<DateTime> ServerNow { get; set; } = () => DateTime.UtcNow;
    /// <summary>The calling student's live submission per assignment.</summary>
    public Dictionary<Guid, SubmissionDto> Mine { get; } = [];
    public List<SubmitAssignmentRequest> SubmitRequests { get; } = [];

    /// <summary>The problem code a submit would get right now, or null when it is allowed (mirrors the server rules).</summary>
    private string? CannotSubmit(CourseAssignmentDto a, SubmissionDto? mine)
    {
        var now = ServerNow();
        if (!a.Module.IsVisibleNow) return ProblemCodes.submission_closed;
        if (a.DueAtUtc is { } due && now > due && a.LatePolicy == LatePolicies.Block) return ProblemCodes.submission_closed;
        if (mine is { Status: var st } && SubmissionStatuses.HandedIn.Contains(st))
        {
            var allowed = a.ResubmissionRule switch
            {
                ResubmissionRules.UntilDue => a.DueAtUtc is null || now <= a.DueAtUtc,
                ResubmissionRules.UntilGraded => st is SubmissionStatuses.Submitted or SubmissionStatuses.Late,
                _ => false,
            };
            if (!allowed) return ProblemCodes.resubmission_not_allowed;
        }
        return null;
    }

    public Task<MySubmissionDto> GetMySubmissionAsync(Guid assignmentId, CancellationToken ct = default) =>
        Run("my-submission", () =>
        {
            if (!Assignments.TryGetValue(assignmentId, out var a)) throw NotFound();
            Mine.TryGetValue(assignmentId, out var mine);
            // A graded submission stays private until the teacher returns it.
            if (mine is { Status: SubmissionStatuses.Graded }) mine = mine with { Grade = null };
            var code = CannotSubmit(a, Mine.GetValueOrDefault(assignmentId));
            return new MySubmissionDto(a with { SubmittedCount = null, GradedCount = null, CanEdit = false, ServerNowUtc = ServerNow() },
                mine, code is null, code, ServerNow());
        });

    public Task<SubmissionDto> SubmitAsync(Guid assignmentId, SubmitAssignmentRequest r, CancellationToken ct = default) =>
        Run("submit", () =>
        {
            SubmitRequests.Add(r);
            if (!Assignments.TryGetValue(assignmentId, out var a)) throw NotFound();
            Mine.TryGetValue(assignmentId, out var mine);
            if (CannotSubmit(a, mine) is { } code) throw new ApiProblemException(409, code, null, null);
            var hasText = !string.IsNullOrWhiteSpace(r.TextHtml);
            if ((!hasText && r.FileAssetIds.Count == 0) || (hasText && !a.AllowText) || (r.FileAssetIds.Count > 0 && !a.AllowFiles) || r.FileAssetIds.Count > 10)
                throw Problem(ProblemCodes.validation_failed);
            var now = ServerNow();
            var late = a.DueAtUtc is { } d && now > d;
            var status = r.SaveAsDraft ? SubmissionStatuses.Draft : late ? SubmissionStatuses.Late : SubmissionStatuses.Submitted;
            var files = r.FileAssetIds.Select(id => new AssetDto(id, a.ClassCourseId, AssetKinds.Document, "file-" + id.ToString("N")[..6] + ".pdf",
                "application/pdf", 1, null, true, now)).ToList();
            return Mine[assignmentId] = new SubmissionDto(mine?.Id ?? Guid.NewGuid(), assignmentId, Guid.NewGuid(), "أنا", "Me", status,
                r.TextHtml, files, r.SaveAsDraft ? null : now, !r.SaveAsDraft && late, (mine?.Number ?? 0) + 1, null);
        });
}
