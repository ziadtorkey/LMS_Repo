using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Messaging;

namespace Lms.Client.Services.Messaging;

/// <summary>
/// Message moderation (M6, FR-MSG-04, D66, D70, L16): the Org Admin works the queue of reports. A moderator reads a report with its frozen context only (the reported message
/// and a few messages around it), never the whole conversation, and a report never names the reporter. Every read of a detail and every attachment URL is audited by the server.
/// </summary>
public interface IModerationApi
{
    /// <summary>Status null = the pending ones; safety concerns first, then the oldest.</summary>
    Task<PagedResult<MessageReportListItemDto>> ListReportsAsync(MessageReportQuery query, CancellationToken ct = default);

    /// <summary>The first read of an open report moves it to InReview.</summary>
    Task<MessageReportDto> GetReportAsync(Guid id, CancellationToken ct = default);

    /// <summary>A restricting action needs <c>DurationDays</c> of 1, 7 or 30; the moderator private note is not a field.</summary>
    Task<MessageReportDto> ResolveReportAsync(Guid id, ResolveReportRequest request, CancellationToken ct = default);

    /// <summary>Dismisses up to the bulk limit; reports outside the scope are skipped, never an error.</summary>
    Task BulkDismissAsync(BulkDismissRequest request, CancellationToken ct = default);

    Task<ModerationStatsDto> GetStatsAsync(CancellationToken ct = default);

    /// <summary>A signed URL for an attachment of the reported message only.</summary>
    Task<ModerationAttachmentUrlDto> GetAttachmentAsync(Guid reportId, Guid fileId, CancellationToken ct = default);

    Task<PagedResult<RestrictionDto>> ListRestrictionsAsync(int page = 1, CancellationToken ct = default);

    /// <summary>Lifts a restriction at once; a block is never touched.</summary>
    Task LiftRestrictionAsync(Guid userId, CancellationToken ct = default);
}

public sealed class ModerationApi(ApiHttp http) : IModerationApi
{
    public Task<PagedResult<MessageReportListItemDto>> ListReportsAsync(MessageReportQuery query, CancellationToken ct = default)
    {
        var parts = new List<string> { $"page={query.Page}", $"pageSize={query.PageSize}" };
        if (!string.IsNullOrWhiteSpace(query.Status)) parts.Add($"status={Uri.EscapeDataString(query.Status)}");
        if (!string.IsNullOrWhiteSpace(query.Reason)) parts.Add($"reason={Uri.EscapeDataString(query.Reason)}");
        return http.GetAsync<PagedResult<MessageReportListItemDto>>($"{Routes.ModerationReports}?{string.Join('&', parts)}", ct);
    }

    public Task<MessageReportDto> GetReportAsync(Guid id, CancellationToken ct = default) => http.GetAsync<MessageReportDto>(Routes.ModerationReport(id), ct);

    public async Task<MessageReportDto> ResolveReportAsync(Guid id, ResolveReportRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<MessageReportDto>(HttpMethod.Post, Routes.ModerationReportResolve(id), request, ct))!;

    public Task BulkDismissAsync(BulkDismissRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.ModerationReportsBulkDismiss, request, ct);

    public Task<ModerationStatsDto> GetStatsAsync(CancellationToken ct = default) => http.GetAsync<ModerationStatsDto>(Routes.ModerationStats, ct);

    public Task<ModerationAttachmentUrlDto> GetAttachmentAsync(Guid reportId, Guid fileId, CancellationToken ct = default) =>
        http.GetAsync<ModerationAttachmentUrlDto>(Routes.ModerationReportAttachment(reportId, fileId), ct);

    public Task<PagedResult<RestrictionDto>> ListRestrictionsAsync(int page = 1, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<RestrictionDto>>($"{Routes.ModerationRestrictions}?page={page}", ct);

    public Task LiftRestrictionAsync(Guid userId, CancellationToken ct = default) => http.SendAsync(HttpMethod.Delete, Routes.ModerationRestriction(userId), null, ct);
}
