using Lms.Shared;
using Lms.Shared.Messaging;

namespace Lms.Client.Services.Messaging;

/// <summary>
/// Organisation announcements (M6, FR-MSG-05) under <c>/api/comms/announcements</c> (the M4a course board keeps <c>/api/announcements</c>), and the reader side under
/// <c>/api/me/announcements</c>. The author needs <c>Announcements.Manage</c> at the authoring unit; a reader sees only their own recipient rows.
/// </summary>
public interface IAnnouncementsApi
{
    Task<PagedResult<AnnouncementListItemDto>> ListAsync(AnnouncementQuery query, CancellationToken ct = default);
    Task<AnnouncementDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<AnnouncementDto> CreateAsync(CreateAnnouncementRequest request, CancellationToken ct = default);

    /// <summary>A draft or scheduled announcement; 409 <c>announcement_not_editable</c> once published.</summary>
    Task<AnnouncementDto> UpdateAsync(Guid id, UpdateAnnouncementRequest request, CancellationToken ct = default);

    /// <summary>Drafts only.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Publishes a draft now, or schedules it when it has a publish time in the future (the server clock decides).</summary>
    Task<AnnouncementDto> PublishAsync(Guid id, CancellationToken ct = default);

    Task<AnnouncementDto> WithdrawAsync(Guid id, CancellationToken ct = default);

    /// <summary>How many people the targets and role filters reach, before publishing.</summary>
    Task<AudiencePreviewDto> PreviewAudienceAsync(AudiencePreviewRequest request, CancellationToken ct = default);

    /// <summary>The reader side: active announcements addressed to the caller, pinned first.</summary>
    Task<PagedResult<MyAnnouncementDto>> ListMineAsync(int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default);

    Task MarkReadAsync(Guid id, CancellationToken ct = default);
}

public sealed class AnnouncementsApi(Api.ApiHttp http) : IAnnouncementsApi
{
    public Task<PagedResult<AnnouncementListItemDto>> ListAsync(AnnouncementQuery query, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<AnnouncementListItemDto>>(
            $"{Routes.OrgAnnouncements}?page={query.Page}&pageSize={query.PageSize}{(string.IsNullOrWhiteSpace(query.Status) ? string.Empty : $"&status={Uri.EscapeDataString(query.Status)}")}", ct);

    public Task<AnnouncementDto> GetAsync(Guid id, CancellationToken ct = default) => http.GetAsync<AnnouncementDto>(Routes.OrgAnnouncement(id), ct);

    public async Task<AnnouncementDto> CreateAsync(CreateAnnouncementRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<AnnouncementDto>(HttpMethod.Post, Routes.OrgAnnouncements, request, ct))!;

    public async Task<AnnouncementDto> UpdateAsync(Guid id, UpdateAnnouncementRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<AnnouncementDto>(HttpMethod.Put, Routes.OrgAnnouncement(id), request, ct))!;

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => http.SendAsync(HttpMethod.Delete, Routes.OrgAnnouncement(id), null, ct);

    public async Task<AnnouncementDto> PublishAsync(Guid id, CancellationToken ct = default) =>
        (await http.SendAsync<AnnouncementDto>(HttpMethod.Post, Routes.OrgAnnouncementPublish(id), null, ct))!;

    public async Task<AnnouncementDto> WithdrawAsync(Guid id, CancellationToken ct = default) =>
        (await http.SendAsync<AnnouncementDto>(HttpMethod.Post, Routes.OrgAnnouncementWithdraw(id), null, ct))!;

    public async Task<AudiencePreviewDto> PreviewAudienceAsync(AudiencePreviewRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<AudiencePreviewDto>(HttpMethod.Post, Routes.OrgAnnouncementAudiencePreview, request, ct))!;

    public Task<PagedResult<MyAnnouncementDto>> ListMineAsync(int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<MyAnnouncementDto>>($"{Routes.MyAnnouncements}?page={page}&pageSize={pageSize}", ct);

    public Task MarkReadAsync(Guid id, CancellationToken ct = default) => http.SendAsync(HttpMethod.Post, Routes.MyAnnouncementRead(id), null, ct);
}
