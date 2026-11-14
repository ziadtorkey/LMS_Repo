using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Domain.Announcements;

/// <summary>
/// A targeted announcement (FR-MSG-03, M6). It belongs to its authoring unit; who it reaches is decided by its targets and role filters and frozen
/// into recipient rows when it is published. Titles are bilingual and both required; a body is Arabic and/or English (sanitised HTML). A draft or a
/// scheduled announcement may be edited freely; once published only the expiry and the pin change, and a body edit leaves the "edited" marker.
/// </summary>
[Audited]
public sealed class Announcement : UnitOwnedEntity
{
    private Announcement()
    {
        TitleAr = TitleEn = Status = RoleFiltersCsv = string.Empty;
    }

    public Announcement(OrgUnit authoringUnit, Guid authorUserId)
    {
        PlaceIn(authoringUnit);
        AuthorUserId = authorUserId;
        Status = AnnouncementStates.Draft;
        TitleAr = TitleEn = RoleFiltersCsv = string.Empty;
    }

    public Guid AuthorUserId { get; private set; }
    public string TitleAr { get; private set; }
    public string TitleEn { get; private set; }
    public string? BodyAr { get; private set; }
    public string? BodyEn { get; private set; }
    public string Status { get; private set; }
    public string RoleFiltersCsv { get; private set; }
    public DateTime? PublishAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public bool IsPinned { get; private set; }
    public bool Notify { get; private set; }
    public bool IsUrgent { get; private set; }
    public int? AudienceCount { get; private set; }
    public DateTime? EditedAtUtc { get; private set; }

    public IReadOnlyList<string> RoleFilters =>
        RoleFiltersCsv.Length == 0 ? [] : RoleFiltersCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>True while the content may still change freely.</summary>
    public bool IsEditable => Status is AnnouncementStates.Draft or AnnouncementStates.Scheduled;

    /// <summary>Replaces the content of a draft or scheduled announcement. The caller has validated and sanitised it.</summary>
    public void SetContent(
        string titleAr, string titleEn, string? bodyAr, string? bodyEn, IEnumerable<string> roleFilters, DateTime? publishAtUtc, DateTime? expiresAtUtc,
        bool pinned, bool notify, bool urgent)
    {
        TitleAr = titleAr.Trim();
        TitleEn = titleEn.Trim();
        BodyAr = bodyAr;
        BodyEn = bodyEn;
        RoleFiltersCsv = string.Join(',', roleFilters.Distinct());
        PublishAtUtc = publishAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        IsPinned = pinned;
        Notify = notify;
        IsUrgent = urgent;
    }

    public void Schedule(DateTime publishAtUtc)
    {
        Status = AnnouncementStates.Scheduled;
        PublishAtUtc = publishAtUtc;
    }

    /// <summary>The audience snapshot is taken; from now on only the expiry and the pin change.</summary>
    public void MarkPublished(DateTime now, int audienceCount)
    {
        Status = AnnouncementStates.Published;
        PublishedAtUtc = now;
        AudienceCount = audienceCount;
    }

    public void Withdraw() => Status = AnnouncementStates.Withdrawn;

    public void Expire() => Status = AnnouncementStates.Expired;

    /// <summary>After publishing only the expiry and the pin change; a changed body sets the visible marker (no new notification).</summary>
    public void EditPublished(string? bodyAr, string? bodyEn, DateTime? expiresAtUtc, bool pinned, DateTime now)
    {
        if (BodyAr != bodyAr || BodyEn != bodyEn)
        {
            BodyAr = bodyAr;
            BodyEn = bodyEn;
            EditedAtUtc = now;
        }

        ExpiresAtUtc = expiresAtUtc;
        IsPinned = pinned;
    }
}

/// <summary>The status strings of <c>Announcement.Status</c> (equal to the contract list <c>Lms.Shared.Messaging.AnnouncementStatuses</c>).</summary>
public static class AnnouncementStates
{
    public const string Draft = "Draft";
    public const string Scheduled = "Scheduled";
    public const string Published = "Published";
    public const string Withdrawn = "Withdrawn";
    public const string Expired = "Expired";
}
