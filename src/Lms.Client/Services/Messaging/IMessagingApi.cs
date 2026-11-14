using Lms.Shared;
using Lms.Shared.Messaging;

namespace Lms.Client.Services.Messaging;

/// <summary>
/// Conversations and messages (M6, FR-MSG-01..03). A conversation is visible only to its participants: anyone else, an Org Admin included, gets 404.
/// A rule failure is 403 <c>messaging_not_allowed</c>; the list endpoints are paged; the realtime event carries ids only (the client reads the message).
/// </summary>
public interface IMessagingApi
{
    Task<PagedResult<ConversationListItemDto>> ListConversationsAsync(int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default);

    /// <summary>A direct conversation between the same pair answers with the existing one.</summary>
    Task<ConversationDto> CreateConversationAsync(CreateConversationRequest request, CancellationToken ct = default);

    Task<ConversationDto> GetConversationAsync(Guid id, CancellationToken ct = default);

    /// <summary>Newest first, paged backwards with <see cref="MessagePageDto.NextBeforeId"/>.</summary>
    Task<MessagePageDto> GetMessagesAsync(Guid conversationId, MessageQuery query, CancellationToken ct = default);

    Task<MessageDto> SendAsync(Guid conversationId, SendMessageRequest request, CancellationToken ct = default);

    /// <summary>The sender deletes their own message for everyone inside the delete window; there is no edit.</summary>
    Task DeleteMessageAsync(Guid conversationId, Guid messageId, CancellationToken ct = default);

    Task MarkReadAsync(Guid conversationId, CancellationToken ct = default);

    Task<IReadOnlyList<MessageSearchResultDto>> SearchAsync(string query, Guid? conversationId = null, int page = 1, CancellationToken ct = default);

    Task ReportAsync(Guid messageId, ReportMessageRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<BlockDto>> ListBlocksAsync(CancellationToken ct = default);
    Task BlockAsync(Guid userId, CancellationToken ct = default);
    Task UnblockAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Only the people the caller may message under the client rules.</summary>
    Task<IReadOnlyList<RecipientDto>> GetRecipientsAsync(string? search = null, CancellationToken ct = default);

    Task<MessagingRulesSummaryDto> GetRulesSummaryAsync(CancellationToken ct = default);
    Task<MessagingLimitsDto> GetLimitsAsync(CancellationToken ct = default);

    /// <summary>The badge: how many conversations have unread messages.</summary>
    Task<UnreadTotalDto> GetUnreadTotalAsync(CancellationToken ct = default);
}

public sealed class MessagingApi(Api.ApiHttp http) : IMessagingApi
{
    public Task<PagedResult<ConversationListItemDto>> ListConversationsAsync(int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<ConversationListItemDto>>($"{Routes.Conversations}?page={page}&pageSize={pageSize}", ct);

    public async Task<ConversationDto> CreateConversationAsync(CreateConversationRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ConversationDto>(HttpMethod.Post, Routes.Conversations, request, ct))!;

    public Task<ConversationDto> GetConversationAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<ConversationDto>(Routes.Conversation(id), ct);

    public Task<MessagePageDto> GetMessagesAsync(Guid conversationId, MessageQuery query, CancellationToken ct = default) =>
        http.GetAsync<MessagePageDto>(Routes.ConversationMessages(conversationId, query.BeforeId, query.PageSize), ct);

    public async Task<MessageDto> SendAsync(Guid conversationId, SendMessageRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<MessageDto>(HttpMethod.Post, Routes.ConversationMessages(conversationId), request, ct))!;

    public Task DeleteMessageAsync(Guid conversationId, Guid messageId, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.Message(conversationId, messageId), null, ct);

    public Task MarkReadAsync(Guid conversationId, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.ConversationRead(conversationId), null, ct);

    public async Task<IReadOnlyList<MessageSearchResultDto>> SearchAsync(string query, Guid? conversationId = null, int page = 1, CancellationToken ct = default) =>
        await http.GetAsync<List<MessageSearchResultDto>>(Routes.MessageSearchUrl(query, conversationId, page), ct);

    public Task ReportAsync(Guid messageId, ReportMessageRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.MessageReport(messageId), request, ct);

    public async Task<IReadOnlyList<BlockDto>> ListBlocksAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<BlockDto>>(Routes.MyBlocks, ct);

    public Task BlockAsync(Guid userId, CancellationToken ct = default) => http.SendAsync(HttpMethod.Put, Routes.MyBlock(userId), null, ct);

    public Task UnblockAsync(Guid userId, CancellationToken ct = default) => http.SendAsync(HttpMethod.Delete, Routes.MyBlock(userId), null, ct);

    public async Task<IReadOnlyList<RecipientDto>> GetRecipientsAsync(string? search = null, CancellationToken ct = default) =>
        await http.GetAsync<List<RecipientDto>>(string.IsNullOrWhiteSpace(search) ? Routes.MessagingRecipients : Routes.MessagingRecipientsSearch(search), ct);

    public Task<MessagingRulesSummaryDto> GetRulesSummaryAsync(CancellationToken ct = default) =>
        http.GetAsync<MessagingRulesSummaryDto>(Routes.MessagingRulesSummary, ct);

    public Task<MessagingLimitsDto> GetLimitsAsync(CancellationToken ct = default) =>
        http.GetAsync<MessagingLimitsDto>(Routes.MessagingLimits, ct);

    public Task<UnreadTotalDto> GetUnreadTotalAsync(CancellationToken ct = default) =>
        http.GetAsync<UnreadTotalDto>(Routes.MyUnreadTotal, ct);
}
