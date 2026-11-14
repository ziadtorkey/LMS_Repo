using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Excuses;

namespace Lms.Client.Services.Excuses;

/// <summary>
/// In-memory excuses for <c>Lms:UseFakeApi=true</c> and the tests. It enforces the state machine the UI relies on: a decision or a withdrawal
/// only from Submitted (Escalated can still be decided), a comment to reject, a reviewer who is not the submitter.
/// </summary>
public sealed class FakeExcusesApi : IExcusesApi
{
    public static readonly Guid Student = Guid.Parse("50000000-0000-0000-0000-000000000001");
    public static readonly Guid Guardian = Guid.Parse("60000000-0000-0000-0000-000000000001");
    public static readonly Guid Counselor = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid Session = Guid.Parse("a0000000-0000-0000-0000-000000000009");

    private readonly TimeProvider _clock;
    private readonly List<ExcuseDto> _excuses = [];

    public FakeExcusesApi() : this(TimeProvider.System) { }

    public FakeExcusesApi(TimeProvider clock) => _clock = clock;

    /// <summary>The signed-in user (submitter or reviewer).</summary>
    public Guid CurrentUser { get; set; } = Guardian;

    /// <summary>Under GuardiansOnly a student cannot submit; the server answers 403 <c>excuse_submitter_not_allowed</c>.</summary>
    public bool CurrentUserIsStudent { get; set; }

    public IReadOnlyList<ExcuseDto> All => _excuses;

    public Task<ExcusePolicyDto> GetPolicyAsync(Guid studentId, CancellationToken ct = default) => Task.FromResult(new ExcusePolicyDto(
        studentId, "GuardiansOnly", !CurrentUserIsStudent, 5, ["Illness", "Family", "Official", "Other"], ["Illness"], 5, ["image/jpeg", "image/png", "application/pdf"], Now));

    public Task<IReadOnlyList<ExcusableSessionDto>> GetExcusableSessionsAsync(Guid studentId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ExcusableSessionDto>>(
        [
            // the same sessions the fake attendance lists as excusable, on the first day of the range
            new ExcusableSessionDto(Guid.Parse("a0000000-0000-0000-0000-000000000011"), from, "الرياضيات", "Mathematics", new TimeOnly(7, 30), new TimeOnly(8, 15), "Absent"),
            new ExcusableSessionDto(Guid.Parse("a0000000-0000-0000-0000-000000000012"), from, "العلوم", "Science", new TimeOnly(8, 20), new TimeOnly(9, 5), "Late"),
        ]);

    public Task<ExcuseAttachmentDto> UploadAttachmentAsync(string fileName, string contentType, Stream content, CancellationToken ct = default) =>
        Task.FromResult(new ExcuseAttachmentDto(Guid.NewGuid(), fileName, contentType, content.CanSeek ? content.Length : 0));

    public Task<ExcuseDto> SubmitAsync(SubmitExcuseRequest request, CancellationToken ct = default)
    {
        if (CurrentUserIsStudent) throw Problem(403, "excuse_submitter_not_allowed");
        var excuse = new ExcuseDto(Guid.NewGuid(), request.StudentId, "أحمد سعيد", "Ahmed Saeed", "S-1001", CurrentUser, "ولي الأمر", "Guardian", request.Reason, request.ReasonText,
            request.DateFrom, request.DateTo, ExcuseStatuses.Submitted, [new ExcuseSessionDto(Session, request.DateFrom ?? DateOnly.FromDateTime(Now), "الرياضيات", "Mathematics", "Absent")],
            request.AttachmentFileId is not null, null, null, null, null, null, null, null, null, Now, null, true,
            [new ExcuseHistoryDto(ExcuseStatuses.Submitted, CurrentUser, "ولي الأمر", "Guardian", null, Now)], Now);
        _excuses.Add(excuse);
        return Task.FromResult(excuse);
    }

    public Task<ExcuseDto> WithdrawAsync(Guid excuseId, CancellationToken ct = default)
    {
        var excuse = Find(excuseId);
        if (excuse.SubmittedByUserId != CurrentUser) throw Problem(404, "excuse_not_found");
        if (excuse.Status != ExcuseStatuses.Submitted) throw Problem(409, "excuse_not_pending");
        return Task.FromResult(Update(excuse, ExcuseStatuses.Withdrawn, null));
    }

    public Task<PagedResult<ExcuseDto>> GetMineAsync(MyExcusesQuery query, CancellationToken ct = default)
    {
        var rows = _excuses.Where(e => (query.StudentId is null || e.StudentId == query.StudentId) && (query.Status is null || e.Status == query.Status)).ToList();
        return Task.FromResult(new PagedResult<ExcuseDto>(rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), rows.Count, query.Page, query.PageSize));
    }

    public Task<PagedResult<ExcuseReviewItemDto>> GetReviewQueueAsync(ExcuseReviewQuery query, CancellationToken ct = default)
    {
        var rows = _excuses.Where(e => query.Status is null || e.Status == query.Status)
            .Select(e => new ExcuseReviewItemDto(e, [], (int)(Now - e.SubmittedAtUtc).TotalDays)).ToList();
        return Task.FromResult(new PagedResult<ExcuseReviewItemDto>(rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), rows.Count, query.Page, query.PageSize));
    }

    public Task<ExcuseDto> ApproveAsync(Guid excuseId, ApproveExcuseRequest request, CancellationToken ct = default) => Task.FromResult(Decide(excuseId, ExcuseStatuses.Approved, request.Comment));

    public Task<ExcuseDto> RejectAsync(Guid excuseId, RejectExcuseRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Comment)) throw Problem(400, "excuse_comment_required");
        return Task.FromResult(Decide(excuseId, ExcuseStatuses.Rejected, request.Comment));
    }

    public Task<ExcuseDto> EscalateAsync(Guid excuseId, EscalateExcuseRequest request, CancellationToken ct = default)
    {
        var excuse = Find(excuseId);
        if (excuse.Status != ExcuseStatuses.Submitted) throw Problem(409, "excuse_not_pending");
        return Task.FromResult(Update(excuse, ExcuseStatuses.Escalated, request.Comment, e => e with { EscalatedToUserId = request.TargetUserId }));
    }

    public Task<IReadOnlyList<EscalationTargetDto>> GetEscalationTargetsAsync(Guid excuseId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EscalationTargetDto>>([new EscalationTargetDto(Guid.Parse("70000000-0000-0000-0000-000000000002"), "المدير", "Manager", "مدير", "Manager", true)]);

    public Task<ExcuseAttachmentUrlDto> GetAttachmentUrlAsync(Guid excuseId, CancellationToken ct = default) =>
        Task.FromResult(new ExcuseAttachmentUrlDto("https://files.example.test/excuse.pdf", Now.AddMinutes(5), "excuse.pdf", "application/pdf", false));

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private ExcuseDto Find(Guid id) => _excuses.FirstOrDefault(e => e.Id == id) ?? throw Problem(404, "excuse_not_found");

    private ExcuseDto Decide(Guid id, string status, string? comment)
    {
        var excuse = Find(id);
        if (excuse.SubmittedByUserId == CurrentUser) throw Problem(409, "excuse_reviewer_is_submitter");
        if (excuse.Status is not (ExcuseStatuses.Submitted or ExcuseStatuses.Escalated)) throw Problem(409, "excuse_not_pending");
        return Update(excuse, status, comment, e => e with { DecisionComment = comment, ReviewerUserId = CurrentUser, DecidedAtUtc = Now });
    }

    private ExcuseDto Update(ExcuseDto excuse, string status, string? comment, Func<ExcuseDto, ExcuseDto>? more = null)
    {
        var updated = excuse with
        {
            Status = status,
            CanWithdraw = false,
            History = [.. excuse.History, new ExcuseHistoryDto(status, CurrentUser, "المستخدم", "User", comment, Now)],
        };
        if (more is not null) updated = more(updated);
        _excuses[_excuses.IndexOf(excuse)] = updated;
        return updated;
    }

    private static ApiProblemException Problem(int status, string code) => new(status, code, null, null);
}
