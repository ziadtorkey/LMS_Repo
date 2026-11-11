using Lms.Domain.Common;

namespace Lms.Domain.People;

/// <summary>One row of the append-only status history of a profile. Never updated, never soft-deleted.</summary>
public sealed class PersonStatusChange : Entity, IOrgUnitScoped
{
    private PersonStatusChange()
    {
        OrgUnitPath = Reason = string.Empty;
    }

    private PersonStatusChange(
        PersonKind kind, Guid profileId, Guid organizationId, Guid orgUnitId, string orgUnitPath, PersonStatus from,
        PersonStatus to, string reason, Guid? changedBy, DateTime changedAt)
    {
        Kind = kind;
        ProfileId = profileId;
        OrganizationId = organizationId;
        OrgUnitId = orgUnitId;
        OrgUnitPath = orgUnitPath;
        From = from;
        To = to;
        Reason = reason;
        ChangedBy = changedBy;
        ChangedAt = changedAt;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public PersonKind Kind { get; private set; }
    public Guid ProfileId { get; private set; }
    public PersonStatus From { get; private set; }
    public PersonStatus To { get; private set; }
    public string Reason { get; private set; }
    public Guid? ChangedBy { get; private set; }
    public DateTime ChangedAt { get; private set; }

    public static PersonStatusChange For(
        StaffProfile profile, PersonStatus from, PersonStatus to, string reason, Guid? changedBy, DateTime changedAt) =>
        new(PersonKind.Staff, profile.Id, profile.OrganizationId, profile.OrgUnitId, profile.OrgUnitPath, from, to, reason, changedBy, changedAt);

    public static PersonStatusChange For(
        StudentProfile profile, PersonStatus from, PersonStatus to, string reason, Guid? changedBy, DateTime changedAt) =>
        new(PersonKind.Student, profile.Id, profile.OrganizationId, profile.OrgUnitId, profile.OrgUnitPath, from, to, reason, changedBy, changedAt);
}
