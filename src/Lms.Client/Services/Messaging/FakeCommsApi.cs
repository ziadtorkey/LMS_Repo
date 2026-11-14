using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;

namespace Lms.Client.Services.Messaging;

/// <summary>
/// The saved messaging rules of the fake client, shared by the messaging fake (who may start a conversation) and the comms fake (the matrix an admin edits).
/// The matrix may only be narrowed: a cell looser than its default is refused, as the server does (D69, 400 <c>rules_looser_than_default</c>).
/// </summary>
public sealed class FakeMessagingRules
{
    private readonly Dictionary<(string From, string To), string> _saved = [];

    public string Mode(string from, string to) => _saved.TryGetValue((from, to), out var mode) ? mode : MessagingRuleDefaults.Default(from, to);

    public MessagingRulesDto Get() => new([.. MessagingRuleDefaults.Roles.SelectMany(f => MessagingRuleDefaults.Roles.Select(t => new MessagingRuleCellDto(f, t, Mode(f, t))))],
        MessagingRuleDefaults.Matrix());

    public void Save(IReadOnlyList<MessagingRuleCellDto> cells)
    {
        if (MessagingRuleDefaults.Loosened(cells).FirstOrDefault() is { } loose) throw new ApiProblemException(400, ProblemCodes.rules_looser_than_default, null, null, $"{loose.FromRole}>{loose.ToRole}");
        foreach (var c in cells) _saved[(c.FromRole, c.ToRole)] = c.Mode;
    }
}

/// <summary>
/// In-memory preferences, channels, push devices, administration and moderation for the standalone dev server and bUnit tests: a mandatory event cannot be
/// switched off (409 <c>notification_mandatory</c>), the test email is limited to three a day (429 <c>rate_limited</c>), the rules are narrow-only.
/// </summary>
public sealed class FakeCommsApi(FakeMessagingRules rules) : ICommsApi
{
    private readonly Dictionary<(string Event, string Channel), bool> _user = [];
    private readonly List<PushSubscriptionDto> _push = [];
    private int _emailTests;

    public FakeCommsApi() : this(new FakeMessagingRules()) { }

    public FakeMessagingRules Rules => rules;
    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;
    public SavePreferencesRequest? LastPreferences { get; private set; }

    public List<ContactChannelDto> Channels { get; } =
    [
        new(NotificationChannels.InApp, ChannelStates.Active, null, true),
        new(NotificationChannels.Email, ChannelStates.Active, "s***@example.com", true),
        new(NotificationChannels.Push, ChannelStates.NeedsSetup, null, false),
    ];

    public CommsSettingsDto Settings { get; set; } = new(true, true, 2000, 5000, 10, 20, true, "21:00", "07:00", 90, false, 20, 20, 50, 20);

    public List<ProviderStatusDto> Providers { get; } =
    [
        new(NotificationChannels.Email, true, true, null, null),
        new(NotificationChannels.Push, true, true, null, null),
    ];

    /// <summary>D76: no SMS in v1. Email, push and the in-app inbox are the only channels a user sees or sets.</summary>
    public static IReadOnlyList<string> Offered { get; } = NotificationChannels.All;

    private static ApiProblemException Problem(int status, string code) => new(status, code, null, null);

    public Task<IReadOnlyList<EffectivePreferenceDto>> GetEffectivePreferencesAsync(CancellationToken ct = default)
    {
        var rows = new List<EffectivePreferenceDto>();
        foreach (var e in NotificationEvents.All)
        {
            foreach (var channel in Offered)
            {
                var isDefault = (e.DefaultChannels ?? [NotificationChannels.InApp]).Contains(channel);
                if (e.Mandatory && isDefault) rows.Add(new(e.Key, channel, true, PreferenceSources.Mandatory, true));
                else if (_user.TryGetValue((e.Key, channel), out var on)) rows.Add(new(e.Key, channel, on, PreferenceSources.User, false));
                else rows.Add(new(e.Key, channel, isDefault, PreferenceSources.Default, false));
            }
        }

        return Task.FromResult<IReadOnlyList<EffectivePreferenceDto>>(rows);
    }

    public Task SavePreferencesAsync(SavePreferencesRequest request, CancellationToken ct = default)
    {
        LastPreferences = request;
        foreach (var p in request.Preferences)
        {
            var def = NotificationEvents.Find(p.EventKey) ?? throw Problem(400, ProblemCodes.validation_failed);
            if (!NotificationChannels.All.Contains(p.Channel)) throw Problem(400, ProblemCodes.validation_failed);
            if (def.Mandatory && !p.Enabled && (def.DefaultChannels ?? []).Contains(p.Channel)) throw Problem(409, ProblemCodes.notification_mandatory);
        }

        foreach (var p in request.Preferences) _user[(p.EventKey, p.Channel)] = p.Enabled;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ContactChannelDto>> GetChannelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ContactChannelDto>>([.. Channels]);

    public Task<TestEmailResultDto> SendTestEmailAsync(CancellationToken ct = default)
    {
        if (_emailTests >= MessagingLimits.EmailTestsPerDay) throw Problem(429, ProblemCodes.rate_limited);
        _emailTests++;
        return Task.FromResult(new TestEmailResultDto(true, null));
    }

    public Task<IReadOnlyList<PushSubscriptionDto>> GetPushSubscriptionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PushSubscriptionDto>>([.. _push]);

    public Task<PushSubscriptionDto> AddPushSubscriptionAsync(SavePushSubscriptionRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint) || request.Endpoint.Length > MessagingLimits.PushEndpointMaxLength) throw Problem(400, ProblemCodes.validation_failed);
        var sub = new PushSubscriptionDto(Guid.NewGuid(), request.DeviceLabel ?? "Browser", Now(), null);
        _push.Add(sub);
        return Task.FromResult(sub);
    }

    public Task RemovePushSubscriptionAsync(Guid id, CancellationToken ct = default)
    {
        if (_push.RemoveAll(s => s.Id == id) == 0) throw Problem(404, ProblemCodes.not_found); // own subscription only
        return Task.CompletedTask;
    }

    public Task<MessagingRulesDto> GetMessagingRulesAsync(CancellationToken ct = default) => Task.FromResult(rules.Get());

    /// <summary>The cells of the last save request, and a failure the next save throws (tests simulate a refusal).</summary>
    public SaveMessagingRulesRequest? LastRules { get; private set; }

    public ApiProblemException? RulesFailure { get; set; }

    public Task<MessagingRulesDto> SaveMessagingRulesAsync(SaveMessagingRulesRequest request, CancellationToken ct = default)
    {
        LastRules = request;
        if (RulesFailure is { } failure) throw failure;
        rules.Save(request.Cells);
        return Task.FromResult(rules.Get());
    }

    public Task<CommsSettingsDto> GetSettingsAsync(CancellationToken ct = default) => Task.FromResult(Settings);

    /// <summary>A failure the next settings save throws (tests simulate the server naming a field).</summary>
    public ApiProblemException? SettingsFailure { get; set; }

    public Task<CommsSettingsDto> SaveSettingsAsync(SaveCommsSettingsRequest request, CancellationToken ct = default)
    {
        if (SettingsFailure is { } failure) throw failure;
        // The server checks every cap against its ceiling and names the field (400 validation_failed).
        var s = request.Settings;
        foreach (var (field, key, value) in new (string, string, int)[]
        {
            ("MaxEmailPerDay", Lms.Shared.Configuration.SettingKeys.CommsMaxEmailPerDay.Name, s.MaxEmailPerDay), ("MaxPushPerDay", Lms.Shared.Configuration.SettingKeys.CommsMaxPushPerDay.Name, s.MaxPushPerDay),
            ("MaxEmailPerUserPerDay", Lms.Shared.Configuration.SettingKeys.CommsMaxEmailPerUserPerDay.Name, s.MaxEmailPerUserPerDay),
            ("MaxPushPerUserPerDay", Lms.Shared.Configuration.SettingKeys.CommsMaxPushPerUserPerDay.Name, s.MaxPushPerUserPerDay),
            ("DeliveryRetentionDays", Lms.Shared.Configuration.SettingKeys.CommsDeliveryRetentionDays.Name, s.DeliveryRetentionDays),
            ("MaxMessagesPerMinute", Lms.Shared.Configuration.SettingKeys.MessagingMaxPerMinute.Name, s.MaxMessagesPerMinute),
            ("MaxNewConversationsPerDay", Lms.Shared.Configuration.SettingKeys.MessagingMaxNewConversationsPerDay.Name, s.MaxNewConversationsPerDay),
            ("MaxGroupSize", Lms.Shared.Configuration.SettingKeys.MessagingMaxGroupSize.Name, s.MaxGroupSize),
            ("MaxAnnouncementsPerAuthorPerDay", Lms.Shared.Configuration.SettingKeys.AnnouncementsMaxPerAuthorPerDay.Name, s.MaxAnnouncementsPerAuthorPerDay),
        })
        {
            var def = Lms.Shared.Configuration.SettingCatalog.Find(key);
            if (def is not null && (value < (def.Min ?? 0) || value > (def.Max ?? int.MaxValue))) throw new ApiProblemException(400, ProblemCodes.validation_failed, null, null, field);
        }
        return Task.FromResult(Settings = request.Settings);
    }

    public Task<IReadOnlyList<ProviderStatusDto>> GetProviderStatusAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProviderStatusDto>>([.. Providers]);
}
