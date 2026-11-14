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

public sealed class ExcusesApi(ApiHttp http) : IExcusesApi
{
    public Task<ExcusePolicyDto> GetPolicyAsync(Guid studentId, CancellationToken ct = default) =>
        http.GetAsync<ExcusePolicyDto>(Routes.StudentExcusePolicy(studentId), ct);

    public async Task<IReadOnlyList<ExcusableSessionDto>> GetExcusableSessionsAsync(Guid studentId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        await http.GetAsync<List<ExcusableSessionDto>>(Routes.StudentExcusableSessions(studentId, from, to), ct);

    public Task<ExcuseAttachmentDto> UploadAttachmentAsync(string fileName, string contentType, Stream content, CancellationToken ct = default) =>
        http.UploadAsync<ExcuseAttachmentDto>(Routes.ExcuseAttachmentsPattern, fileName, contentType, content, null, ct);

    public async Task<ExcuseDto> SubmitAsync(SubmitExcuseRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ExcuseDto>(HttpMethod.Post, Routes.ExcusesPattern, request, ct))!;

    public async Task<ExcuseDto> WithdrawAsync(Guid excuseId, CancellationToken ct = default) =>
        (await http.SendAsync<ExcuseDto>(HttpMethod.Post, Routes.ExcuseWithdraw(excuseId), null, ct))!;

    public Task<PagedResult<ExcuseDto>> GetMineAsync(MyExcusesQuery query, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<ExcuseDto>>(Routes.MeExcuses + Query(("studentId", query.StudentId), ("status", query.Status), ("page", query.Page), ("pageSize", query.PageSize)), ct);

    public Task<PagedResult<ExcuseReviewItemDto>> GetReviewQueueAsync(ExcuseReviewQuery query, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<ExcuseReviewItemDto>>(Routes.ExcuseReviewPattern + Query(("status", query.Status), ("unitId", query.UnitId), ("studentId", query.StudentId),
            ("from", query.From is { } f ? f.ToString("yyyy-MM-dd") : null), ("to", query.To is { } t ? t.ToString("yyyy-MM-dd") : null), ("page", query.Page), ("pageSize", query.PageSize)), ct);

    public async Task<ExcuseDto> ApproveAsync(Guid excuseId, ApproveExcuseRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ExcuseDto>(HttpMethod.Post, Routes.ExcuseApprove(excuseId), request, ct))!;

    public async Task<ExcuseDto> RejectAsync(Guid excuseId, RejectExcuseRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ExcuseDto>(HttpMethod.Post, Routes.ExcuseReject(excuseId), request, ct))!;

    public async Task<ExcuseDto> EscalateAsync(Guid excuseId, EscalateExcuseRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ExcuseDto>(HttpMethod.Post, Routes.ExcuseEscalate(excuseId), request, ct))!;

    public async Task<IReadOnlyList<EscalationTargetDto>> GetEscalationTargetsAsync(Guid excuseId, CancellationToken ct = default) =>
        await http.GetAsync<List<EscalationTargetDto>>(Routes.ExcuseEscalationTargets(excuseId), ct);

    public Task<ExcuseAttachmentUrlDto> GetAttachmentUrlAsync(Guid excuseId, CancellationToken ct = default) =>
        http.GetAsync<ExcuseAttachmentUrlDto>(Routes.ExcuseAttachmentUrl(excuseId), ct);

    private static string Query(params (string Key, object? Value)[] parts)
    {
        var query = string.Join('&', parts.Where(p => p.Value is not null).Select(p => $"{p.Key}={Uri.EscapeDataString(Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture)!)}"));
        return query.Length == 0 ? "" : "?" + query;
    }
}
