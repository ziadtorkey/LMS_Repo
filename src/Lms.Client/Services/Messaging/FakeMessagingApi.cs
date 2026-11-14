using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;

namespace Lms.Client.Services.Messaging;

/// <summary>A person of the fake directory: enough to apply the messaging rules (role, unit) and to show a name.</summary>
public sealed record FakeMessagingUser(Guid Id, string NameAr, string NameEn, string RoleKey, Guid UnitId);

/// <summary>
/// In-memory messaging for the standalone dev server and bUnit tests, mirroring the server rules of #613 section 9: a conversation exists only for its
/// participants (anyone else gets 404), the rules matrix decides who may start a conversation (403 <c>messaging_not_allowed</c>), a message is deleted by its
/// sender inside the delete window only, and a block silently stops direct messages both ways.
/// </summary>
public sealed class FakeMessagingApi : IMessagingApi
{
    public static readonly Guid UnitA = Guid.Parse("00000000-0000-0000-0000-0000000a0001");
    public static readonly Guid UnitB = Guid.Parse("00000000-0000-0000-0000-0000000a0002");

    public static readonly FakeMessagingUser Sara = new(Guid.Parse("00000000-0000-0000-0000-0000000b0001"), "سارة", "Sara", BuiltInRoles.Student, UnitA);
    public static readonly FakeMessagingUser Omar = new(Guid.Parse("00000000-0000-0000-0000-0000000b0002"), "عمر", "Omar", BuiltInRoles.Student, UnitA);
    public static readonly FakeMessagingUser Huda = new(Guid.Parse("00000000-0000-0000-0000-0000000b0003"), "هدى", "Huda", BuiltInRoles.Teacher, UnitA);
    public static readonly FakeMessagingUser Khaled = new(Guid.Parse("00000000-0000-0000-0000-0000000b0004"), "خالد", "Khaled", BuiltInRoles.Guardian, UnitA);
    public static readonly FakeMessagingUser Layla = new(Guid.Parse("00000000-0000-0000-0000-0000000b0005"), "ليلى", "Layla", BuiltInRoles.Teacher, UnitB);
    public static readonly FakeMessagingUser Admin = new(Guid.Parse("00000000-0000-0000-0000-0000000b0006"), "المدير", "Admin", BuiltInRoles.OrgAdmin, UnitA);

    private sealed class Conversation
    {
        public Guid Id = Guid.NewGuid();
        public Guid OwnerId;
        public Guid? ClassCourseId;
        public HashSet<Guid> Left = [];
        public Dictionary<Guid, DateTime?> Muted = [];
        public string Kind = ConversationKinds.Direct;
        public string? Title;
        public List<Guid> Participants = [];
        public bool Locked;
        public DateTime CreatedAtUtc;
        public Dictionary<Guid, Guid?> LastRead = [];
        public List<Message> Messages = [];
    }

    private sealed record Message(Guid Id, Guid SenderId, string Body, DateTime CreatedAtUtc, HashSet<Guid> HiddenFrom)
    {
        public bool Deleted { get; set; }
    }

    private readonly List<Conversation> _conversations = [];
    private readonly HashSet<(Guid By, Guid Blocked)> _blocks = [];
    private readonly List<(Guid MessageId, ReportMessageRequest Request)> _reports = [];

    public FakeMessagingApi() : this(new FakeMessagingRules()) { }

    public FakeMessagingApi(FakeMessagingRules rules)
    {
        Rules = rules;
        Users = [Sara, Omar, Huda, Khaled, Layla, Admin];
        Me = Sara;
        Related.Add((Sara.Id, Huda.Id)); // the relationship rule: a teacher of the student
        Related.Add((Huda.Id, Sara.Id));
        Related.Add((Khaled.Id, Huda.Id));
        Related.Add((Huda.Id, Khaled.Id));
    }

    public FakeMessagingRules Rules { get; }
    public List<FakeMessagingUser> Users { get; }

    /// <summary>The caller of the fake (change it to read the same data as another participant).</summary>
    public FakeMessagingUser Me { get; set; }

    /// <summary>Pairs that satisfy the Relationship rule (teaches, is guardian of) in either direction.</summary>
    public HashSet<(Guid, Guid)> Related { get; } = [];

    /// <summary>The fake server clock (the delete window is judged by it).</summary>
    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    public MessagingLimitsDto Limits { get; set; } = new(MessagingLimits.MaxBodyLength, MessagingLimits.MaxAttachmentsPerMessage, MessagingLimits.MaxAttachmentMb,
        MessagingLimits.DefaultMaxGroupSize, MessagingLimits.DeleteWindowMinutes, 20, false);

    public IReadOnlyList<(Guid MessageId, ReportMessageRequest Request)> Reports => _reports;

    private static ApiProblemException Problem(int status, string code) => new(status, code, null, null);

    private FakeMessagingUser User(Guid id) => Users.First(u => u.Id == id);

    private static readonly string[] StaffRoles = [BuiltInRoles.Teacher, BuiltInRoles.Counselor, BuiltInRoles.Supervisor, BuiltInRoles.Manager, BuiltInRoles.OrgAdmin];

    private ParticipantDto ToParticipant(Conversation c, Guid id) => new(id, User(id).NameAr, User(id).NameEn, User(id).RoleKey, c.Left.Contains(id));

    /// <summary>D74, D77: students read a group and never post in it (they reply privately to the teacher); guardians may post in a teacher-run group that includes them.</summary>
    private bool ReadOnlyForMe(Conversation c) => c.Kind == ConversationKinds.Group && Me.RoleKey == BuiltInRoles.Student;

    /// <summary>A conversation the caller takes part in; anyone else gets 404, as the server answers (an Org Admin included).</summary>
    private Conversation Mine(Guid id) =>
        _conversations.FirstOrDefault(c => c.Id == id && c.Participants.Contains(Me.Id) && !c.Left.Contains(Me.Id)) ?? throw Problem(404, ProblemCodes.not_found); // a participant who left gets 404 too

    private IEnumerable<Message> Visible(Conversation c) => c.Messages.Where(m => !m.HiddenFrom.Contains(Me.Id));

    private bool Allowed(FakeMessagingUser from, FakeMessagingUser to) => Rules.Mode(from.RoleKey, to.RoleKey) switch
    {
        MessagingRuleModes.Allow => true,
        MessagingRuleModes.SameUnit => from.UnitId == to.UnitId,
        MessagingRuleModes.Relationship => Related.Contains((from.Id, to.Id)),
        _ => false,
    };

    private MessageDto ToDto(Conversation c, Message m)
    {
        var sender = User(m.SenderId);
        var canDelete = m.SenderId == Me.Id && !m.Deleted && Now() <= m.CreatedAtUtc.AddMinutes(Limits.DeleteWindowMinutes);
        return new MessageDto(m.Id, c.Id, m.SenderId, sender.NameAr, sender.NameEn, m.Deleted ? null : m.Body,
            m.Deleted ? MessageStates.Deleted : MessageStates.Normal, [], m.CreatedAtUtc, m.Deleted ? Now() : null, canDelete);
    }

    private ConversationDto ToDto(Conversation c) =>
        new(c.Id, c.Kind, c.Title, [.. c.Participants.Select(id => ToParticipant(c, id))], c.Locked, c.Muted.ContainsKey(Me.Id), c.CreatedAtUtc, ReadOnlyForMe(c), c.OwnerId, c.ClassCourseId);

    public Task<PagedResult<ConversationListItemDto>> ListConversationsAsync(int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default)
    {
        var mine = _conversations.Where(c => c.Participants.Contains(Me.Id) && !c.Left.Contains(Me.Id)).ToList();
        var items = mine.Select(c =>
        {
            var visible = Visible(c).ToList();
            var lastRead = c.LastRead.GetValueOrDefault(Me.Id);
            var unreadFrom = lastRead is null ? 0 : visible.FindIndex(m => m.Id == lastRead) + 1;
            var unread = visible.Skip(unreadFrom).Count(m => m.SenderId != Me.Id && !m.Deleted);
            var last = visible.LastOrDefault();
            return new ConversationListItemDto(c.Id, c.Kind, c.Title, [.. c.Participants.Select(id => ToParticipant(c, id))], unread,
                last is null || last.Deleted ? null : last.Body, last?.CreatedAtUtc, c.Muted.ContainsKey(Me.Id), c.Locked, ReadOnlyForMe(c)); // a muted conversation still counts its unread
        }).OrderByDescending(i => i.LastMessageAtUtc ?? DateTime.MinValue).ToList();
        return Task.FromResult(new PagedResult<ConversationListItemDto>([.. items.Skip((page - 1) * pageSize).Take(pageSize)], items.Count, page, pageSize));
    }

    public Task<ConversationDto> CreateConversationAsync(CreateConversationRequest request, CancellationToken ct = default)
    {
        var others = request.ParticipantUserIds.Distinct().Where(id => id != Me.Id).ToList();
        if (others.Count == 0 || others.Any(id => Users.All(u => u.Id != id))) throw Problem(400, ProblemCodes.validation_failed);
        if (request.Kind == ConversationKinds.Direct && others.Count != 1) throw Problem(400, ProblemCodes.validation_failed);
        if (request.Kind == ConversationKinds.Group && others.Count + 1 > Limits.MaxGroupSize) throw Problem(400, ProblemCodes.validation_failed);
        if (request.Kind == ConversationKinds.Group && !StaffRoles.Contains(Me.RoleKey)) throw Problem(403, ProblemCodes.messaging_not_allowed); // only staff create groups (D69)
        if (others.Any(id => !Allowed(Me, User(id)))) throw Problem(403, ProblemCodes.messaging_not_allowed); // the rules matrix, per participant

        if (request.Kind == ConversationKinds.Direct)
        {
            var existing = _conversations.FirstOrDefault(c => c.Kind == ConversationKinds.Direct && c.Participants.Contains(Me.Id) && c.Participants.Contains(others[0]));
            if (existing is not null) return Task.FromResult(ToDto(existing)); // the same pair: the existing conversation (200)
        }

        var conversation = new Conversation { Kind = request.Kind, Title = request.Title, Participants = [Me.Id, .. others], CreatedAtUtc = Now(), OwnerId = Me.Id, ClassCourseId = request.ClassCourseId };
        _conversations.Add(conversation);
        if (!string.IsNullOrWhiteSpace(request.FirstMessage)) AddMessage(conversation, request.FirstMessage);
        return Task.FromResult(ToDto(conversation));
    }

    public Task<ConversationDto> GetConversationAsync(Guid id, CancellationToken ct = default) => Task.FromResult(ToDto(Mine(id)));

    public Task<MessagePageDto> GetMessagesAsync(Guid conversationId, MessageQuery query, CancellationToken ct = default)
    {
        var c = Mine(conversationId);
        var all = Visible(c).OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id).ToList();
        var start = query.BeforeId is { } before ? all.FindIndex(m => m.Id == before) + 1 : 0;
        var page = all.Skip(start).Take(query.PageSize).ToList();
        var next = start + page.Count < all.Count ? page.LastOrDefault()?.Id : null;
        return Task.FromResult(new MessagePageDto([.. page.Select(m => ToDto(c, m))], next));
    }

    private Message AddMessage(Conversation c, string body)
    {
        var hidden = new HashSet<Guid>();
        if (c.Kind == ConversationKinds.Direct)
        {
            // A block stops direct messages both ways and the blocked person is never told: the message is kept for the sender only.
            var other = c.Participants.First(p => p != Me.Id);
            if (_blocks.Contains((other, Me.Id))) hidden.Add(other);
        }

        var message = new Message(Guid.NewGuid(), Me.Id, body.Trim(), Now(), hidden);
        c.Messages.Add(message);
        c.LastRead[Me.Id] = message.Id;
        return message;
    }

    public Task<MessageDto> SendAsync(Guid conversationId, SendMessageRequest request, CancellationToken ct = default)
    {
        var c = Mine(conversationId);
        if (c.Locked) throw Problem(409, ProblemCodes.conversation_closed);
        if (ReadOnlyForMe(c)) throw Problem(403, ProblemCodes.group_read_only);
        if (string.IsNullOrWhiteSpace(request.Body)) throw Problem(400, ProblemCodes.validation_failed);
        if (request.Body.Length > Limits.MaxBodyLength) throw Problem(400, ProblemCodes.message_too_long);
        if ((request.AttachmentFileIds?.Count ?? 0) > Limits.MaxAttachments) throw Problem(400, ProblemCodes.validation_failed);
        return Task.FromResult(ToDto(c, AddMessage(c, request.Body)));
    }

    public Task DeleteMessageAsync(Guid conversationId, Guid messageId, CancellationToken ct = default)
    {
        var c = Mine(conversationId);
        var m = c.Messages.FirstOrDefault(x => x.Id == messageId) ?? throw Problem(404, ProblemCodes.not_found);
        if (m.SenderId != Me.Id) throw Problem(404, ProblemCodes.not_found); // sender only: for anyone else the message is not theirs to touch
        if (Now() > m.CreatedAtUtc.AddMinutes(Limits.DeleteWindowMinutes)) throw Problem(409, ProblemCodes.message_delete_window_closed);
        m.Deleted = true;
        return Task.CompletedTask;
    }

    public Task MarkReadAsync(Guid conversationId, CancellationToken ct = default)
    {
        var c = Mine(conversationId);
        c.LastRead[Me.Id] = Visible(c).LastOrDefault()?.Id;
        return Task.CompletedTask;
    }

    public Task<ConversationDto> MuteAsync(Guid conversationId, MuteConversationRequest request, CancellationToken ct = default)
    {
        var c = Mine(conversationId);
        if (request.UntilUtc is { } until && until <= Now()) throw Problem(400, ProblemCodes.validation_failed); // a date must lie in the future
        c.Muted[Me.Id] = request.UntilUtc;
        return Task.FromResult(ToDto(c));
    }

    public Task UnmuteAsync(Guid conversationId, CancellationToken ct = default)
    {
        Mine(conversationId).Muted.Remove(Me.Id);
        return Task.CompletedTask;
    }

    public Task LeaveAsync(Guid conversationId, CancellationToken ct = default)
    {
        var c = Mine(conversationId);
        if (c.Kind == ConversationKinds.Direct) throw Problem(409, ProblemCodes.conversation_closed); // a direct conversation is muted or blocked, not left
        c.Left.Add(Me.Id);
        return Task.CompletedTask;
    }

    public Task<ConversationDto> AddParticipantsAsync(Guid conversationId, AddParticipantsRequest request, CancellationToken ct = default)
    {
        var c = Mine(conversationId);
        if (c.Kind != ConversationKinds.Group || c.OwnerId != Me.Id) throw Problem(404, ProblemCodes.not_found); // the owner only; another participant gets 404
        var add = request.UserIds.Distinct().Where(id => !c.Participants.Contains(id) || c.Left.Contains(id)).ToList();
        if (add.Any(id => Users.All(u => u.Id != id))) throw Problem(400, ProblemCodes.validation_failed);
        if (add.Any(id => !Allowed(Me, User(id)))) throw Problem(403, ProblemCodes.messaging_not_allowed); // owner to member, never pairwise
        if (c.Participants.Count(p => !c.Left.Contains(p)) + add.Count > Limits.MaxGroupSize) throw Problem(400, ProblemCodes.validation_failed);
        foreach (var id in add)
        {
            c.Left.Remove(id);
            if (!c.Participants.Contains(id)) c.Participants.Add(id);
        }

        return Task.FromResult(ToDto(c));
    }

    public Task RemoveParticipantAsync(Guid conversationId, Guid userId, CancellationToken ct = default)
    {
        var c = Mine(conversationId);
        if (c.Kind != ConversationKinds.Group || c.OwnerId != Me.Id) throw Problem(404, ProblemCodes.not_found);
        if (userId == Me.Id || !c.Participants.Contains(userId)) throw Problem(400, ProblemCodes.validation_failed); // the owner leaves with Leave
        if (c.Participants.Count(p => !c.Left.Contains(p)) <= 3) throw Problem(400, ProblemCodes.validation_failed); // a group never drops below three
        c.Left.Add(userId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MessageSearchResultDto>> SearchAsync(string query, Guid? conversationId = null, int page = 1, CancellationToken ct = default)
    {
        if (query.Trim().Length < MessagingLimits.SearchMinLength) throw Problem(400, ProblemCodes.validation_failed);
        var hits = _conversations.Where(c => c.Participants.Contains(Me.Id) && (conversationId is null || c.Id == conversationId))
            .SelectMany(c => Visible(c).Where(m => !m.Deleted && m.Body.Contains(query, StringComparison.OrdinalIgnoreCase)).Select(m => (c, m)))
            .OrderByDescending(x => x.m.CreatedAtUtc).Take(MessagingLimits.SearchMaxResults)
            .Select(x => new MessageSearchResultDto(x.m.Id, x.c.Id, x.m.Body, User(x.m.SenderId).NameAr, User(x.m.SenderId).NameEn, x.m.CreatedAtUtc)).ToList();
        return Task.FromResult<IReadOnlyList<MessageSearchResultDto>>(hits);
    }

    public Task ReportAsync(Guid messageId, ReportMessageRequest request, CancellationToken ct = default)
    {
        var c = _conversations.FirstOrDefault(x => x.Participants.Contains(Me.Id) && x.Messages.Any(m => m.Id == messageId)) ?? throw Problem(404, ProblemCodes.not_found);
        if (c.Messages.First(m => m.Id == messageId).SenderId == Me.Id) throw Problem(404, ProblemCodes.not_found); // a participant who is not the sender
        if (_reports.Any(r => r.MessageId == messageId)) throw Problem(409, ProblemCodes.report_duplicate);
        _reports.Add((messageId, request));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BlockDto>> ListBlocksAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BlockDto>>([.. _blocks.Where(b => b.By == Me.Id).Select(b => new BlockDto(b.Blocked, User(b.Blocked).NameAr, User(b.Blocked).NameEn, Now()))]);

    public Task BlockAsync(Guid userId, CancellationToken ct = default)
    {
        _blocks.Add((Me.Id, userId));
        return Task.CompletedTask;
    }

    public Task UnblockAsync(Guid userId, CancellationToken ct = default)
    {
        _blocks.Remove((Me.Id, userId));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RecipientDto>> GetRecipientsAsync(string? search = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RecipientDto>>([.. Users.Where(u => u.Id != Me.Id && Allowed(Me, u)
            && (string.IsNullOrWhiteSpace(search) || u.NameEn.Contains(search, StringComparison.OrdinalIgnoreCase) || u.NameAr.Contains(search)))
            .Select(u => new RecipientDto(u.Id, u.NameAr, u.NameEn, u.RoleKey, null, null))]);

    public Task<MessagingRulesSummaryDto> GetRulesSummaryAsync(CancellationToken ct = default) =>
        Task.FromResult(new MessagingRulesSummaryDto(
            [.. MessagingRuleDefaults.Roles.Where(r => Rules.Mode(Me.RoleKey, r) != MessagingRuleModes.Deny)],
            StaffRoles.Contains(Me.RoleKey) && MessagingRuleDefaults.Roles.Count(r => Rules.Mode(Me.RoleKey, r) != MessagingRuleModes.Deny) >= 2)); // only staff create groups (D69)

    public Task<MessagingLimitsDto> GetLimitsAsync(CancellationToken ct = default) => Task.FromResult(Limits);

    public Task<UnreadTotalDto> GetUnreadTotalAsync(CancellationToken ct = default)
    {
        var page = ListConversationsAsync(1, 200, ct).GetAwaiter().GetResult();
        return Task.FromResult(new UnreadTotalDto(page.Items.Count(i => i.UnreadCount > 0)));
    }

    /// <summary>A moderator locked the conversation: sending is 409 <c>conversation_closed</c> from now on.</summary>
    public void Lock(Guid conversationId) => _conversations.First(c => c.Id == conversationId).Locked = true;

    /// <summary>Test and demo helper: a message from <paramref name="sender"/> arrives in a conversation the caller is in.</summary>
    public MessageDto Receive(Guid conversationId, FakeMessagingUser sender, string body)
    {
        var c = Mine(conversationId);
        var message = new Message(Guid.NewGuid(), sender.Id, body, Now(), []);
        c.Messages.Add(message);
        var previous = Me;
        Me = sender;
        var dto = ToDto(c, message);
        Me = previous;
        return dto;
    }
}
