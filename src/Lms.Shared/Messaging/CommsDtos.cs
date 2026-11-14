namespace Lms.Shared.Messaging;

/// <summary>
/// One cell of the user's effective notification preferences: whether the event reaches the user on the channel, and why. A mandatory event cannot be switched
/// off (409 <c>notification_mandatory</c>); a user can still opt out of SMS by number. A client admin sets which channels may be used at all; a user can only narrow.
/// </summary>
public sealed record EffectivePreferenceDto(string EventKey, string Channel, bool Enabled, string Source, bool Mandatory);

/// <summary>PUT <c>/api/me/notification-preferences</c>: the cells the user changed.</summary>
public sealed record SavePreferenceRequest(string EventKey, string Channel, bool Enabled);

public sealed record SavePreferencesRequest(IReadOnlyList<SavePreferenceRequest> Preferences);

/// <summary>GET <c>/api/me/channels</c>: where the user's notices go. The address is masked (<c>m***@example.com</c>, <c>+966 5** *** *12</c>); no code is verified in v1 (D66), the flag shows "unverified".</summary>
public sealed record ContactChannelDto(string Channel, string State, string? MaskedAddress, bool Verified);

/// <summary>POST <c>/api/me/channels/email/test</c> (3 a day): <c>Accepted</c> = the relay took it (nothing is promised about delivery).</summary>
public sealed record TestEmailResultDto(bool Accepted, string? Code);

/// <summary>A Web Push subscription of the user's browser (own rows only). <c>DeviceLabel</c> is the user-agent family shown in the list.</summary>
public sealed record PushSubscriptionDto(Guid Id, string DeviceLabel, DateTime CreatedAtUtc, DateTime? LastSuccessUtc);

public sealed record SavePushSubscriptionRequest(string Endpoint, string P256dh, string Auth, string? DeviceLabel);

/// <summary>GET <c>/api/comms/provider-status</c> (<c>Comms.Manage</c>): per channel, whether a provider is configured and whether it is the development fake.</summary>
public sealed record ProviderStatusDto(string Channel, bool Configured, bool IsFake, DateTime? LastSuccessUtc, string? LastErrorCode);

/// <summary>
/// The communication settings of a client (<c>Comms.Manage</c>, client root): which channels are on, the daily caps (client and per recipient, D66 defaults
/// 5,000 email / 500 SMS / 20,000 push), quiet hours (SMS and push only, off by default), the delivery retention and the messaging limits.
/// </summary>
public sealed record CommsSettingsDto(
    bool EmailEnabled,
    bool SmsEnabled,
    bool PushEnabled,
    int MaxEmailPerDay,
    int MaxSmsPerDay,
    int MaxPushPerDay,
    int MaxEmailPerUserPerDay,
    int MaxSmsPerUserPerDay,
    int MaxPushPerUserPerDay,
    bool QuietHoursEnabled,
    string QuietHoursStart,
    string QuietHoursEnd,
    int DeliveryRetentionDays,
    bool ReadReceipts,
    int MaxMessagesPerMinute,
    int MaxNewConversationsPerDay,
    int MaxGroupSize,
    int MaxAnnouncementsPerAuthorPerDay);

/// <summary>PUT <c>/api/comms/settings</c>: the whole document; a value outside its catalog range, a cap above the platform ceiling or a malformed time is 400 <c>validation_failed</c>.</summary>
public sealed record SaveCommsSettingsRequest(CommsSettingsDto Settings);
