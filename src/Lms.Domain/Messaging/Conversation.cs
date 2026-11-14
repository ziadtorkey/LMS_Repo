using Lms.Domain.Common;

namespace Lms.Domain.Messaging;

/// <summary>
/// A conversation between people of one client (FR-MSG-01, D66, D69): direct (two people, unique per pair through <see cref="DirectKey"/>) or a group. Only its
/// participants read it (L6); nobody else, an admin or a moderator included. A direct conversation stores who started it (<see cref="InitiatorUserId"/>, L25) because
/// the rules are checked from the initiator when it is created and when a message is posted.
/// </summary>
public sealed class Conversation : AuditableEntity, ITenantOwned
{
    public const string DirectKind = "Direct";
    public const string GroupKind = "Group";

    private Conversation()
    {
        Kind = string.Empty;
    }

    private Conversation(Guid organizationId, string kind, Guid createdByUserId, DateTime nowUtc)
    {
        OrganizationId = organizationId;
        Kind = kind;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = nowUtc;
    }

    public static Conversation Direct(Guid organizationId, Guid initiator, Guid other, DateTime nowUtc) => new(organizationId, DirectKind, initiator, nowUtc)
    {
        InitiatorUserId = initiator,
        DirectKey = BuildDirectKey(organizationId, initiator, other),
    };

    public static Conversation Group(Guid organizationId, Guid owner, string? title, Guid? classCourseId, DateTime nowUtc) => new(organizationId, GroupKind, owner, nowUtc)
    {
        OwnerUserId = owner,
        Title = title,
        ClassCourseId = classCourseId,
    };

    public Guid OrganizationId { get; private set; }
    public string Kind { get; private set; }
    public string? Title { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>The user who started a direct conversation (L25); null for a group.</summary>
    public Guid? InitiatorUserId { get; private set; }

    /// <summary>The owner of a group; null for a direct conversation.</summary>
    public Guid? OwnerUserId { get; private set; }

    public Guid? ClassCourseId { get; private set; }

    /// <summary>"organization:smallerUserId:largerUserId" for a direct conversation: unique, so a parallel create cannot make a second one.</summary>
    public string? DirectKey { get; private set; }

    /// <summary>A moderator locked it: no new messages (409 <c>conversation_closed</c>).</summary>
    public bool IsLocked { get; private set; }

    /// <summary>The owner set the group read-only for everyone.</summary>
    public bool IsReadOnly { get; private set; }

    public DateTime? LastMessageAtUtc { get; private set; }

    public static string BuildDirectKey(Guid organizationId, Guid a, Guid b)
    {
        var (low, high) = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
        return $"{organizationId:N}:{low:N}:{high:N}";
    }

    public void NoteMessage(DateTime atUtc) => LastMessageAtUtc = atUtc;
}
