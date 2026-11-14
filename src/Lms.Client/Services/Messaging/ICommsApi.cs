using Lms.Shared;
using Lms.Shared.Messaging;

namespace Lms.Client.Services.Messaging;

/// <summary>
/// Notification delivery preferences and channels (every user), and the communication administration (<c>Comms.Manage</c> at the client root): the messaging
/// rules matrix, the delivery settings and the provider status, plus message moderation (<c>Messaging.Moderate</c>).
/// </summary>
public interface ICommsApi
{
    // ---- own preferences, channels and devices

    /// <summary>The effective preference per event and channel, with where it comes from and whether it is mandatory.</summary>
    Task<IReadOnlyList<EffectivePreferenceDto>> GetEffectivePreferencesAsync(CancellationToken ct = default);

    /// <summary>A mandatory event switched off is 409 <c>notification_mandatory</c>.</summary>
    Task SavePreferencesAsync(SavePreferencesRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ContactChannelDto>> GetChannelsAsync(CancellationToken ct = default);

    /// <summary>Sends a test to the own address; three a day, then 429 <c>rate_limited</c>.</summary>
    Task<TestEmailResultDto> SendTestEmailAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PushSubscriptionDto>> GetPushSubscriptionsAsync(CancellationToken ct = default);
    Task<PushSubscriptionDto> AddPushSubscriptionAsync(SavePushSubscriptionRequest request, CancellationToken ct = default);
    Task RemovePushSubscriptionAsync(Guid id, CancellationToken ct = default);

    // ---- administration (client root)

    Task<MessagingRulesDto> GetMessagingRulesAsync(CancellationToken ct = default);

    /// <summary>A rule may only be narrowed: a cell looser than its default is 400 <c>rules_looser_than_default</c> (D69).</summary>
    Task<MessagingRulesDto> SaveMessagingRulesAsync(SaveMessagingRulesRequest request, CancellationToken ct = default);

    Task<CommsSettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task<CommsSettingsDto> SaveSettingsAsync(SaveCommsSettingsRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ProviderStatusDto>> GetProviderStatusAsync(CancellationToken ct = default);
}

public sealed class CommsApi(Api.ApiHttp http) : ICommsApi
{
    public async Task<IReadOnlyList<EffectivePreferenceDto>> GetEffectivePreferencesAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<EffectivePreferenceDto>>(Routes.MyEffectivePreferences, ct);

    public Task SavePreferencesAsync(SavePreferencesRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.MyNotificationPreferences, request, ct);

    public async Task<IReadOnlyList<ContactChannelDto>> GetChannelsAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<ContactChannelDto>>(Routes.MyChannels, ct);

    public async Task<TestEmailResultDto> SendTestEmailAsync(CancellationToken ct = default) =>
        (await http.SendAsync<TestEmailResultDto>(HttpMethod.Post, Routes.MyEmailTest, null, ct))!;

    public async Task<IReadOnlyList<PushSubscriptionDto>> GetPushSubscriptionsAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<PushSubscriptionDto>>(Routes.MyPushSubscriptions, ct);

    public async Task<PushSubscriptionDto> AddPushSubscriptionAsync(SavePushSubscriptionRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<PushSubscriptionDto>(HttpMethod.Post, Routes.MyPushSubscriptions, request, ct))!;

    public Task RemovePushSubscriptionAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.MyPushSubscription(id), null, ct);

    public Task<MessagingRulesDto> GetMessagingRulesAsync(CancellationToken ct = default) => http.GetAsync<MessagingRulesDto>(Routes.CommsMessagingRules, ct);

    public async Task<MessagingRulesDto> SaveMessagingRulesAsync(SaveMessagingRulesRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<MessagingRulesDto>(HttpMethod.Put, Routes.CommsMessagingRules, request, ct))!;

    public Task<CommsSettingsDto> GetSettingsAsync(CancellationToken ct = default) => http.GetAsync<CommsSettingsDto>(Routes.CommsSettings, ct);

    public async Task<CommsSettingsDto> SaveSettingsAsync(SaveCommsSettingsRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<CommsSettingsDto>(HttpMethod.Put, Routes.CommsSettings, request, ct))!;

    public async Task<IReadOnlyList<ProviderStatusDto>> GetProviderStatusAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<ProviderStatusDto>>(Routes.CommsProviderStatus, ct);
}
