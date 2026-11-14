using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Excuses;

namespace Lms.Client.Services.Excuses;

/// <summary>Absence excuses: submit and track (guardian, student), review, escalate and decide (counselor). M5 wave 3, FR-EXC-01..04.</summary>
public interface IExcusesApi
{
    Task<ExcusePolicyDto> GetPolicyAsync(Guid studentId, CancellationToken ct = default);

    Task<IReadOnlyList<ExcusableSessionDto>> GetExcusableSessionsAsync(Guid studentId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<ExcuseAttachmentDto> UploadAttachmentAsync(string fileName, string contentType, Stream content, CancellationToken ct = default);

    Task<ExcuseDto> SubmitAsync(SubmitExcuseRequest request, CancellationToken ct = default);

    Task<ExcuseDto> WithdrawAsync(Guid excuseId, CancellationToken ct = default);

    Task<PagedResult<ExcuseDto>> GetMineAsync(MyExcusesQuery query, CancellationToken ct = default);

    Task<PagedResult<ExcuseReviewItemDto>> GetReviewQueueAsync(ExcuseReviewQuery query, CancellationToken ct = default);

    Task<ExcuseDto> ApproveAsync(Guid excuseId, ApproveExcuseRequest request, CancellationToken ct = default);

    /// <summary>A comment is required (400 <c>excuse_comment_required</c>).</summary>
    Task<ExcuseDto> RejectAsync(Guid excuseId, RejectExcuseRequest request, CancellationToken ct = default);

    Task<ExcuseDto> EscalateAsync(Guid excuseId, EscalateExcuseRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<EscalationTargetDto>> GetEscalationTargetsAsync(Guid excuseId, CancellationToken ct = default);

    Task<ExcuseAttachmentUrlDto> GetAttachmentUrlAsync(Guid excuseId, CancellationToken ct = default);
}
