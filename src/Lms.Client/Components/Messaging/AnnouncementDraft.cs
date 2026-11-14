namespace Lms.Client.Components.Messaging;

/// <summary>The audience kinds of #613 rev 3 (<c>AnnouncementTargetKinds</c>); the composer shows only the kinds the author may use.</summary>
public enum AudienceScopeKind
{
    Organization,
    Unit,
    Level,
    Class,
    ClassCourse,
    Group,
}

/// <summary>Who inside the targets sees an announcement (#613 rev 3 <c>RoleFilter</c>).</summary>
public enum AudienceRole
{
    Staff,
    Students,
    Guardians,
}

public enum PublishMode
{
    Now,
    Schedule,
}

/// <summary>
/// What the announcement composer edits (spec #615 §4.4). A UI model only: the page maps it to the M6 contract. Targets are ids chosen by the page's
/// own target picker, because which targets exist depends on the author's scope.
/// </summary>
public sealed class AnnouncementDraft
{
    public string TitleAr { get; set; } = "";
    public string TitleEn { get; set; } = "";
    public string BodyAr { get; set; } = "";
    public string BodyEn { get; set; } = "";
    public AudienceScopeKind Scope { get; set; } = AudienceScopeKind.ClassCourse;
    public List<Guid> TargetIds { get; } = [];
    public bool IncludeSubtree { get; set; } = true;
    public HashSet<AudienceRole> Roles { get; } = [AudienceRole.Students, AudienceRole.Guardians];
    public PublishMode Mode { get; set; } = PublishMode.Now;

    /// <summary>The local date and time to publish (Schedule only), in the client time zone (D64).</summary>
    public DateTime? PublishAtLocal { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public bool Pin { get; set; }
    public bool Notify { get; set; } = true;
}
