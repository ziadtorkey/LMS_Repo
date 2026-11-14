using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Messaging;

namespace Lms.Client.Services.Messaging;

/// <summary>
/// In-memory announcements mirroring the server rules: only drafts are edited or deleted (409 <c>announcement_not_editable</c> otherwise), a publish with a
/// future time is scheduled, a withdrawn or expired announcement leaves the reader list, and a reader sees pinned items first and only their own read marks.
/// </summary>
public sealed class FakeAnnouncementsApi : IAnnouncementsApi
{
    private readonly List<AnnouncementDto> _items = [];
    private readonly HashSet<Guid> _read = [];

    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    /// <summary>The unit the author may write for; anything else is 403, as the unit access check answers.</summary>
    public Guid AuthoringUnitId { get; set; } = FakeMessagingApi.UnitA;

    /// <summary>How many people each role filter reaches in the demo (the audience preview multiplies out the filters).</summary>
    public Dictionary<string, int> RoleCounts { get; } = new()
    {
        [AnnouncementRoleFilters.Staff] = 12,
        [AnnouncementRoleFilters.Students] = 240,
        [AnnouncementRoleFilters.Guardians] = 310,
    };

    /// <summary>Targets outside the author scope: the server answers 403 for them, as the unit access check does.</summary>
    public HashSet<Guid> OutOfScope { get; } = [];

    public AudiencePreviewRequest? LastPreview { get; private set; }

    private static ApiProblemException Problem(int status, string code) => new(status, code, null, null);

    private AnnouncementDto Find(Guid id) => _items.FirstOrDefault(a => a.Id == id) ?? throw Problem(404, ProblemCodes.not_found);

    private void Replace(AnnouncementDto next) => _items[_items.FindIndex(a => a.Id == next.Id)] = next;

    private void ValidateTargets(IReadOnlyList<AnnouncementTargetDto> targets)
    {
        if (targets.Any(t => t.TargetId is { } id && OutOfScope.Contains(id))) throw Problem(403, "MissingPermission");
        if (targets.Count == 0) throw Problem(400, ProblemCodes.announcement_target_invalid);
        if (targets.Any(t => t.Kind != AnnouncementTargetKinds.Organization && t.TargetId is null)) throw Problem(400, ProblemCodes.announcement_target_invalid);
    }

    private static string Status(DateTime? publishAt, DateTime now, bool publish) =>
        !publish ? AnnouncementStatuses.Draft : publishAt is { } at && at > now ? AnnouncementStatuses.Scheduled : AnnouncementStatuses.Published;

    private int Audience(IReadOnlyList<string>? roles) =>
        (roles is { Count: > 0 } ? roles : [.. RoleCounts.Keys]).Sum(r => RoleCounts.GetValueOrDefault(r));

    public Task<PagedResult<AnnouncementListItemDto>> ListAsync(AnnouncementQuery query, CancellationToken ct = default)
    {
        var rows = _items.Where(a => query.Status is null || a.Status == query.Status).OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new AnnouncementListItemDto(a.Id, a.TitleAr, a.TitleEn, a.Status, a.IsPinned, a.PublishAtUtc, a.PublishedAtUtc, a.ExpiresAtUtc, a.AudienceCount, a.AuthorNameAr, a.AuthorNameEn)).ToList();
        return Task.FromResult(new PagedResult<AnnouncementListItemDto>([.. rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)], rows.Count, query.Page, query.PageSize));
    }

    public Task<AnnouncementDto> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Find(id));

    public Task<AnnouncementDto> CreateAsync(CreateAnnouncementRequest r, CancellationToken ct = default)
    {
        if (r.AuthoringUnitId != AuthoringUnitId) throw Problem(403, "MissingPermission");
        if (string.IsNullOrWhiteSpace(r.TitleAr) || string.IsNullOrWhiteSpace(r.TitleEn) || r.TitleAr.Length > MessagingLimits.MaxAnnouncementTitleLength
            || (r.BodyAr?.Length ?? 0) > MessagingLimits.MaxAnnouncementBodyLength || (r.AttachmentFileIds?.Count ?? 0) > MessagingLimits.MaxAnnouncementAttachments)
            throw Problem(400, ProblemCodes.validation_failed);
        ValidateTargets(r.Targets);
        var now = Now();
        if (r.PublishAtUtc is { } scheduled && scheduled <= now && r.Publish) throw Problem(400, ProblemCodes.validation_failed); // the server clock: a schedule lies in the future
        if (r.ExpiresAtUtc is { } expires && expires <= (r.PublishAtUtc ?? now)) throw Problem(400, ProblemCodes.validation_failed); // expiry follows publish
        var status = Status(r.PublishAtUtc, now, r.Publish);
        var item = new AnnouncementDto(Guid.NewGuid(), r.AuthoringUnitId, r.TitleAr.Trim(), r.TitleEn.Trim(), r.BodyAr, r.BodyEn, status, r.Targets, r.RoleFilters ?? [],
            r.PublishAtUtc, status == AnnouncementStatuses.Published ? now : null, r.ExpiresAtUtc, r.IsPinned, r.Notify, [], Guid.NewGuid(), "المؤلف", "Author",
            status is AnnouncementStatuses.Draft ? null : Audience(r.RoleFilters), null, now);
        _items.Add(item);
        return Task.FromResult(item);
    }

    public Task<AnnouncementDto> UpdateAsync(Guid id, UpdateAnnouncementRequest r, CancellationToken ct = default)
    {
        var a = Find(id);
        if (a.Status is not (AnnouncementStatuses.Draft or AnnouncementStatuses.Scheduled)) throw Problem(409, ProblemCodes.announcement_not_editable);
        ValidateTargets(r.Targets);
        var next = a with
        {
            TitleAr = r.TitleAr, TitleEn = r.TitleEn, BodyAr = r.BodyAr, BodyEn = r.BodyEn, Targets = r.Targets, RoleFilters = r.RoleFilters ?? [],
            PublishAtUtc = r.PublishAtUtc, ExpiresAtUtc = r.ExpiresAtUtc, IsPinned = r.IsPinned, Notify = r.Notify, EditedAtUtc = Now(),
        };
        Replace(next);
        return Task.FromResult(next);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var a = Find(id);
        if (a.Status != AnnouncementStatuses.Draft) throw Problem(409, ProblemCodes.announcement_not_editable);
        _items.Remove(a);
        return Task.CompletedTask;
    }

    public Task<AnnouncementDto> PublishAsync(Guid id, CancellationToken ct = default)
    {
        var a = Find(id);
        if (a.Status != AnnouncementStatuses.Draft) throw Problem(409, ProblemCodes.announcement_not_editable);
        var now = Now();
        var status = Status(a.PublishAtUtc, now, true);
        var next = a with { Status = status, PublishedAtUtc = status == AnnouncementStatuses.Published ? now : null, AudienceCount = Audience(a.RoleFilters) };
        Replace(next);
        return Task.FromResult(next);
    }

    public Task<AnnouncementDto> WithdrawAsync(Guid id, CancellationToken ct = default)
    {
        var a = Find(id);
        if (a.Status is not (AnnouncementStatuses.Published or AnnouncementStatuses.Scheduled)) throw Problem(409, ProblemCodes.announcement_not_editable);
        var next = a with { Status = AnnouncementStatuses.Withdrawn };
        Replace(next);
        return Task.FromResult(next);
    }

    public Task<AudiencePreviewDto> PreviewAudienceAsync(AudiencePreviewRequest request, CancellationToken ct = default)
    {
        LastPreview = request;
        if (request.AuthoringUnitId != AuthoringUnitId) throw Problem(403, "MissingPermission");
        ValidateTargets(request.Targets);
        var sample = new[] { "Sara", "Omar", "Huda", "Khaled" }.Take(MessagingLimits.AudiencePreviewSampleMax).ToList();
        return Task.FromResult(new AudiencePreviewDto(Audience(request.RoleFilters), sample));
    }

    public Task<PagedResult<MyAnnouncementDto>> ListMineAsync(int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default)
    {
        var now = Now();
        var rows = _items.Where(a => a.Status == AnnouncementStatuses.Published && (a.ExpiresAtUtc is null || a.ExpiresAtUtc > now))
            .OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.PublishedAtUtc)
            .Select(a => new MyAnnouncementDto(a.Id, a.TitleAr, a.TitleEn, a.BodyAr, a.BodyEn, a.Attachments, a.AuthorNameAr, a.AuthorNameEn,
                a.PublishedAtUtc ?? a.CreatedAtUtc, a.IsPinned, _read.Contains(a.Id), a.EditedAtUtc)).ToList();
        return Task.FromResult(new PagedResult<MyAnnouncementDto>([.. rows.Skip((page - 1) * pageSize).Take(pageSize)], rows.Count, page, pageSize));
    }

    public Task<IReadOnlyList<MyAnnouncementDto>> ListForCourseAsync(Guid classCourseId, CancellationToken ct = default)
    {
        var now = Now();
        var rows = _items.Where(a => a.Status == AnnouncementStatuses.Published && (a.ExpiresAtUtc is null || a.ExpiresAtUtc > now)
                && a.Targets.Any(t => t.Kind == AnnouncementTargetKinds.ClassCourse && t.TargetId == classCourseId))
            .Select(a => new MyAnnouncementDto(a.Id, a.TitleAr, a.TitleEn, a.BodyAr, a.BodyEn, a.Attachments, a.AuthorNameAr, a.AuthorNameEn,
                a.PublishedAtUtc ?? a.CreatedAtUtc, a.IsPinned, _read.Contains(a.Id), a.EditedAtUtc)).ToList();
        return Task.FromResult<IReadOnlyList<MyAnnouncementDto>>(rows);
    }

    public Task MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var a = Find(id);
        if (a.Status != AnnouncementStatuses.Published) throw Problem(404, ProblemCodes.not_found); // only a recipient row of the caller
        _read.Add(id);
        return Task.CompletedTask;
    }
}
