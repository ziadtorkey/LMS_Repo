using Lms.Shared;
using Lms.Shared.YearEnd;

namespace Lms.Client.Services.YearEnd;

// M7 H1 (FE proposal #808, contract #812): the year-end clients. One interface per module, an HTTP implementation and a fake that mimics the server rules the UI relies on.

/// <summary>Close a year (FR-YEAR-04, D79): what it locks, the close itself, the closing report.</summary>
public interface IYearCloseApi
{
    Task<YearClosePreviewDto> GetPreviewAsync(Guid yearId, CancellationToken ct = default);
    Task<YearClosedDto> CloseAsync(Guid yearId, CloseYearRequest request, CancellationToken ct = default);
    Task<ClosingReportDto> GetReportAsync(Guid yearId, CancellationToken ct = default);
}

/// <summary>Grade changes after a close (FR-GRD-06, D80).</summary>
public interface IGradeChangesApi
{
    Task<GradeChangeRequestDto> CreateAsync(Guid classCourseId, CreateGradeChangeRequest request, CancellationToken ct = default);
    Task<PagedResult<GradeChangeRequestDto>> ListAsync(GradeChangeQuery query, CancellationToken ct = default);
    Task<GradeChangeRequestDto> ApproveAsync(Guid id, DecideGradeChangeRequest request, CancellationToken ct = default);
    Task<GradeChangeRequestDto> RejectAsync(Guid id, DecideGradeChangeRequest request, CancellationToken ct = default);
}

/// <summary>The promotion run (FR-STU-07, D12, D78 to D84).</summary>
public interface IPromotionApi
{
    Task<PromotionRunDto> CreateRunAsync(CreatePromotionRunRequest request, CancellationToken ct = default);
    Task<PromotionRunDto> GetRunAsync(Guid runId, CancellationToken ct = default);
    Task<PagedResult<PromotionRunDto>> ListRunsAsync(int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default);
    Task<PagedResult<PromotionRowDto>> GetRowsAsync(Guid runId, PromotionRowsQuery query, CancellationToken ct = default);
    Task SaveDecisionsAsync(Guid runId, SavePromotionDecisionsRequest request, CancellationToken ct = default);
    Task<PromotionCountsDto> SetOutcomeForFilterAsync(Guid runId, SetPromotionOutcomeForFilterRequest request, CancellationToken ct = default);
    Task<PromotionRunDto> ExecuteAsync(Guid runId, ExecutePromotionRequest request, CancellationToken ct = default);
    Task<PromotionRunDto> UndoAsync(Guid runId, CancellationToken ct = default);
    Task<PagedResult<PromotionFailureDto>> GetFailuresAsync(Guid runId, int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default);
}

/// <summary>Withdrawal, graduation, archive and re-admission (FR-STU-08, D81, D82).</summary>
public interface ILifecycleApi
{
    Task WithdrawAsync(Guid studentId, WithdrawStudentRequest request, CancellationToken ct = default);
    Task GraduateAsync(Guid studentId, GraduateStudentRequest request, CancellationToken ct = default);
    Task ReadmitAsync(Guid studentId, ReadmitStudentRequest request, CancellationToken ct = default);
    Task<PagedResult<ArchivedStudentDto>> GetArchiveAsync(ArchiveQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<WithdrawalReasonDto>> GetReasonsAsync(CancellationToken ct = default);
    Task SaveReasonsAsync(SaveWithdrawalReasonsRequest request, CancellationToken ct = default);
}

/// <summary>Open the next year from a copy or a template (FR-YEAR-05).</summary>
public interface INewYearApi
{
    Task<OpenNextYearPreviewDto> PreviewAsync(Guid fromYearId, OpenNextYearRequest request, CancellationToken ct = default);
    Task<NewYearRunDto> StartAsync(Guid fromYearId, OpenNextYearRequest request, CancellationToken ct = default);
    Task<NewYearRunDto> GetRunAsync(Guid fromYearId, Guid runId, CancellationToken ct = default);
}
