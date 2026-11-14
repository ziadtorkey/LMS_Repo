using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Messaging;

namespace Lms.Client.Services.Messaging;

/// <summary>
/// In-memory moderation mirroring the server rules (D70, L16): only the Org Admin moderates (anyone else 403), a report carries its frozen context and nothing of the rest of
/// the conversation, it never names the reporter, the first read moves Open to InReview (and is counted as an audited read), a restricting action needs 1, 7 or 30 days, a
/// resolved report cannot be resolved again, an attachment is served only for the reported message, and a bulk dismissal takes at most the bulk limit.
/// </summary>
public sealed class FakeModerationApi : IModerationApi
{
    private sealed class Report
    {
        public Guid Id = Guid.NewGuid();
        public Guid MessageId;
        public Guid ConversationId;
        public string Reason = ReportReasons.Other;
        public string? Note;
        public string Status = ReportStatuses.Open;
        public List<ReportContextMessageDto> Context = [];
        public Dictionary<Guid, string> Files = [];
        public SenderSummaryDto Sender = default!;
        public Guid SenderId;
        public DateTime CreatedAtUtc;
        public string? Action;
        public DateTime? ResolvedAtUtc;
    }

    private readonly List<Report> _reports = [];
    private readonly List<RestrictionDto> _restrictions = [];

    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    /// <summary>D70: only the Org Admin moderates; a teacher, a manager or a student gets 403.</summary>
    public bool IsOrgAdmin { get; set; } = true;

    /// <summary>How many times a detail was read: every read is audited by the server.</summary>
    public int AuditedReads { get; private set; }

    public ResolveReportRequest? LastResolve { get; private set; }
    public List<Guid> Skipped { get; } = [];

    private static ApiProblemException Problem(int status, string code) => new(status, code, null, null);

    private void Require()
    {
        if (!IsOrgAdmin) throw Problem(403, "MissingPermission");
    }

    private Report Find(Guid id) => _reports.FirstOrDefault(r => r.Id == id) ?? throw Problem(404, ProblemCodes.not_found);

    /// <summary>Test and demo helper: a report with a frozen context of five messages each side of the reported one.</summary>
    public Guid AddReport(string reason = ReportReasons.Harassment, string reported = "You are useless", string sender = "Omar", int hoursAgo = 3, bool withFile = false)
    {
        var now = Now();
        var messageId = Guid.NewGuid();
        var context = new List<ReportContextMessageDto>();
        for (var i = -ReportCtx; i <= ReportCtx; i++)
        {
            var reportedOne = i == 0;
            context.Add(new ReportContextMessageDto(reportedOne ? messageId : Guid.NewGuid(), reportedOne ? sender : "Sara", reportedOne ? sender : "Sara",
                reportedOne ? reported : "Message " + i, reportedOne && withFile ? ["homework.pdf"] : [], now.AddHours(-hoursAgo).AddMinutes(i), reportedOne));
        }

        var report = new Report
        {
            MessageId = messageId, ConversationId = Guid.NewGuid(), Reason = reason, Note = "Please review", Context = context, CreatedAtUtc = now.AddHours(-hoursAgo),
            Sender = new SenderSummaryDto(sender, sender, "Student", "10-A", "10-A", 2, 1), SenderId = Guid.NewGuid(),
        };
        if (withFile) report.Files[Guid.NewGuid()] = "homework.pdf";
        _reports.Add(report);
        return report.Id;
    }

    private const int ReportCtx = MessagingLimits.ReportContextEachSide;

    public Guid FileOf(Guid reportId) => Find(reportId).Files.Keys.First();

    private static int Rank(string reason) => Array.IndexOf([.. ReportReasons.All], reason);

    public Task<PagedResult<MessageReportListItemDto>> ListReportsAsync(MessageReportQuery query, CancellationToken ct = default)
    {
        Require();
        var now = Now();
        var rows = _reports.Where(r => (query.Status is null ? ReportStatuses.Pending.Contains(r.Status) : r.Status == query.Status) && (query.Reason is null || r.Reason == query.Reason))
            .OrderBy(r => Rank(r.Reason)).ThenBy(r => r.CreatedAtUtc) // safety concerns first, then the oldest
            .Select(r => new MessageReportListItemDto(r.Id, r.Reason, r.Status, r.Sender.NameAr, r.Sender.NameEn, r.Context.First(c => c.IsReported).Body ?? string.Empty,
                r.CreatedAtUtc, (int)(now - r.CreatedAtUtc).TotalHours, r.Reason == ReportReasons.SafetyConcern)).ToList();
        return Task.FromResult(new PagedResult<MessageReportListItemDto>([.. rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)], rows.Count, query.Page, query.PageSize));
    }

    private MessageReportDto ToDto(Report r) => new(r.Id, r.MessageId, r.ConversationId, r.Reason, r.Note, r.Status, r.Context, r.Sender,
        r.Reason == ReportReasons.SafetyConcern ? r.CreatedAtUtc.AddHours(24) : null, r.Action, null, r.ResolvedAtUtc, Now());

    public Task<MessageReportDto> GetReportAsync(Guid id, CancellationToken ct = default)
    {
        Require();
        var r = Find(id);
        AuditedReads++;
        if (r.Status == ReportStatuses.Open) r.Status = ReportStatuses.InReview; // the first read
        return Task.FromResult(ToDto(r));
    }

    public Task<MessageReportDto> ResolveReportAsync(Guid id, ResolveReportRequest request, CancellationToken ct = default)
    {
        Require();
        LastResolve = request;
        var r = Find(id);
        if (!ModerationActions.All.Contains(request.Action)) throw Problem(400, ProblemCodes.validation_failed);
        if (ModerationActions.Restricting.Contains(request.Action) && (request.DurationDays is not { } d || !RestrictionDurations.IsValid(d))) throw Problem(400, ProblemCodes.validation_failed);
        if ((request.NoteToPeople?.Length ?? 0) > ModerationLimits.NoteToPeopleMaxLength) throw Problem(400, ProblemCodes.validation_failed);
        if (r.Status is ReportStatuses.Dismissed or ReportStatuses.ActionTaken) throw Problem(409, ProblemCodes.validation_failed);
        r.Status = request.Action == ModerationActions.Dismiss ? ReportStatuses.Dismissed : ReportStatuses.ActionTaken;
        r.Action = request.Action;
        r.ResolvedAtUtc = Now();
        if (ModerationActions.Restricting.Contains(request.Action))
        {
            _restrictions.Add(new RestrictionDto(r.SenderId, r.Sender.NameAr, r.Sender.NameEn, Now().AddDays(request.DurationDays!.Value), r.Reason, Now()));
        }

        return Task.FromResult(ToDto(r));
    }

    public Task BulkDismissAsync(BulkDismissRequest request, CancellationToken ct = default)
    {
        Require();
        if (request.ReportIds.Count > ModerationLimits.BulkDismissMax) throw Problem(400, ProblemCodes.validation_failed);
        foreach (var id in request.ReportIds)
        {
            var r = _reports.FirstOrDefault(x => x.Id == id);
            if (r is null || r.Status is ReportStatuses.Dismissed or ReportStatuses.ActionTaken) { Skipped.Add(id); continue; } // others are skipped, never an error
            r.Status = ReportStatuses.Dismissed;
            r.Action = ModerationActions.Dismiss;
            r.ResolvedAtUtc = Now();
        }

        return Task.CompletedTask;
    }

    public Task<ModerationStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        if (!IsOrgAdmin) return Task.FromResult(new ModerationStatsDto(0, 0, 0, 0)); // outside the scope the numbers are zero
        var now = Now();
        var decided = _reports.Where(r => r.ResolvedAtUtc is not null).Select(r => (r.ResolvedAtUtc!.Value - r.CreatedAtUtc).TotalHours).Order().ToList();
        return Task.FromResult(new ModerationStatsDto(_reports.Count(r => ReportStatuses.Pending.Contains(r.Status)), _reports.Count(r => r.CreatedAtUtc.Date == now.Date),
            _restrictions.Count(x => x.UntilUtc > now), decided.Count == 0 ? 0 : decided[decided.Count / 2]));
    }

    public Task<ModerationAttachmentUrlDto> GetAttachmentAsync(Guid reportId, Guid fileId, CancellationToken ct = default)
    {
        Require();
        var r = Find(reportId);
        if (!r.Files.TryGetValue(fileId, out var name)) throw Problem(404, ProblemCodes.not_found); // only an attachment of the reported message
        AuditedReads++;
        return Task.FromResult(new ModerationAttachmentUrlDto("https://files.example/" + fileId.ToString("N"), Now().AddMinutes(5), name, "application/pdf", false));
    }

    public Task<PagedResult<RestrictionDto>> ListRestrictionsAsync(int page = 1, CancellationToken ct = default)
    {
        Require();
        var rows = _restrictions.Where(x => x.UntilUtc > Now()).OrderBy(x => x.UntilUtc).ToList();
        return Task.FromResult(new PagedResult<RestrictionDto>(rows, rows.Count, page, Paging.DefaultPageSize));
    }

    public Task LiftRestrictionAsync(Guid userId, CancellationToken ct = default)
    {
        Require();
        if (_restrictions.RemoveAll(x => x.UserId == userId) == 0) throw Problem(404, ProblemCodes.not_found);
        return Task.CompletedTask;
    }
}
