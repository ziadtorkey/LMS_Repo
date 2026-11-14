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
        if (MessagingRuleDefaults.Loosened(cells).Any()) throw new ApiProblemException(400, ProblemCodes.rules_looser_than_default, null, null);
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
    private readonly List<MessageReportDto> _reports = [];
    private int _emailTests;

    public FakeCommsApi() : this(new FakeMessagingRules()) { }

    public FakeMessagingRules Rules => rules;
    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;
    public SavePreferencesRequest? LastPreferences { get; private set; }

    public List<ContactChannelDto> Channels { get; } =
    [
        new(NotificationChannels.InApp, ChannelStates.Active, null, true),
        new(NotificationChannels.Email, ChannelStates.Active, "s***@example.com", true),
        new(NotificationChannels.Sms, ChannelStates.NeedsSetup, null, false),
        new(NotificationChannels.Push, ChannelStates.NeedsSetup, null, false),
    ];

    public CommsSettingsDto Settings { get; set; } = new(true, false, true, 2000, 0, 5000, 10, 0, 20, true, "21:00", "07:00", 90, false, 20, 20, 50, 20);

    public List<ProviderStatusDto> Providers { get; } =
    [
        new(NotificationChannels.Email, true, true, null, null),
        new(NotificationChannels.Sms, false, true, null, null),
        new(NotificationChannels.Push, true, true, null, null),
    ];

    private static ApiProblemException Problem(int status, string code) => new(status, code, null, null);

    public Task<IReadOnlyList<EffectivePreferenceDto>> GetEffectivePreferencesAsync(CancellationToken ct = default)
    {
        var rows = new List<EffectivePreferenceDto>();
        foreach (var e in NotificationEvents.All)
        {
            foreach (var channel in NotificationChannels.All)
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

    public Task<MessagingRulesDto> SaveMessagingRulesAsync(SaveMessagingRulesRequest request, CancellationToken ct = default)
    {
        rules.Save(request.Cells);
        return Task.FromResult(rules.Get());
    }

    public Task<CommsSettingsDto> GetSettingsAsync(CancellationToken ct = default) => Task.FromResult(Settings);

    public Task<CommsSettingsDto> SaveSettingsAsync(SaveCommsSettingsRequest request, CancellationToken ct = default)
    {
        if (request.Settings.MaxGroupSize < 2 || request.Settings.MaxMessagesPerMinute < 1) throw Problem(400, ProblemCodes.validation_failed);
        return Task.FromResult(Settings = request.Settings);
    }

    public Task<IReadOnlyList<ProviderStatusDto>> GetProviderStatusAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProviderStatusDto>>([.. Providers]);

    /// <summary>Test and demo helper: an open report appears in the moderation queue.</summary>
    public MessageReportDto AddReport(Guid messageId, Guid conversationId, string body) =>
        Add(new MessageReportDto(Guid.NewGuid(), messageId, conversationId, ReportReasons.Harassment, null, ReportStatuses.Open, Guid.NewGuid(), Guid.NewGuid(),
            "مستخدم", "A user", body, [], Now(), null, null, null));

    private MessageReportDto Add(MessageReportDto report)
    {
        _reports.Add(report);
        return report;
    }

    public Task<PagedResult<MessageReportDto>> ListReportsAsync(ReportQuery query, CancellationToken ct = default)
    {
        var rows = _reports.Where(r => query.Status is null || r.Status == query.Status).ToList();
        return Task.FromResult(new PagedResult<MessageReportDto>([.. rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)], rows.Count, query.Page, query.PageSize));
    }

    public Task<MessageReportDto> GetReportAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_reports.FirstOrDefault(r => r.Id == id) ?? throw Problem(404, ProblemCodes.not_found));

    public Task<MessageReportDto> ResolveReportAsync(Guid id, ResolveReportRequest request, CancellationToken ct = default)
    {
        var r = _reports.FirstOrDefault(x => x.Id == id) ?? throw Problem(404, ProblemCodes.not_found);
        if (r.Status != ReportStatuses.Open) throw Problem(409, ProblemCodes.validation_failed);
        var resolved = r with
        {
            Status = request.Action == ModerationActions.Dismiss ? ReportStatuses.Dismissed : ReportStatuses.Actioned,
            Action = request.Action, ResolutionNote = request.Note, ResolvedAtUtc = Now(),
        };
        _reports[_reports.IndexOf(r)] = resolved;
        return Task.FromResult(resolved);
    }

    public Task<ModerationStatsDto> GetModerationStatsAsync(CancellationToken ct = default) =>
        Task.FromResult(new ModerationStatsDto(_reports.Count(r => r.Status == ReportStatuses.Open), _reports.Count(r => r.Status != ReportStatuses.Open), 0));
}
