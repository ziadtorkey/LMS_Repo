using Lms.Shared.Authorization;

namespace Lms.Shared.Messaging;

/// <summary>
/// The D69 safeguarding-first default matrix of who may write to whom, by built-in role (sender row, recipient column). A client may only <b>narrow</b>
/// it (a cell above its default on the order <c>Deny &lt; Relationship &lt; SameUnit &lt; Allow</c> is refused, and the policy evaluates <c>min(stored, default)</c> so a stored looser row never counts) (<c>PUT /api/comms/messaging-rules</c> answers 400 <c>rules_looser_than_default</c> for a looser cell): a student reaches only their own teachers and
/// counselor, never another student; a guardian reaches only the teachers and counselor of their own children, never another guardian; a manager or an
/// Org Admin reaches staff and guardians and a student only through a group or an announcement (no private one-to-one with a student).
/// </summary>
public static class MessagingRuleDefaults
{
    /// <summary>The roles that appear in the matrix.</summary>
    public static IReadOnlyList<string> Roles { get; } =
    [
        BuiltInRoles.Student, BuiltInRoles.Guardian, BuiltInRoles.Teacher, BuiltInRoles.Counselor,
        BuiltInRoles.Supervisor, BuiltInRoles.Manager, BuiltInRoles.OrgAdmin,
    ];

    private static readonly HashSet<string> Staff =
        [BuiltInRoles.Teacher, BuiltInRoles.Counselor, BuiltInRoles.Supervisor, BuiltInRoles.Manager, BuiltInRoles.OrgAdmin];

    /// <summary>The default mode of one cell. Unknown roles are denied.</summary>
    public static string Default(string from, string to)
    {
        if (!Roles.Contains(from) || !Roles.Contains(to))
        {
            return MessagingRuleModes.Deny;
        }

        return from switch
        {
            BuiltInRoles.Student => to is BuiltInRoles.Teacher or BuiltInRoles.Counselor ? MessagingRuleModes.Relationship : MessagingRuleModes.Deny,
            BuiltInRoles.Guardian => to is BuiltInRoles.Teacher or BuiltInRoles.Counselor ? MessagingRuleModes.Relationship : MessagingRuleModes.Deny,
            BuiltInRoles.Teacher => to is BuiltInRoles.Student or BuiltInRoles.Guardian ? MessagingRuleModes.Relationship
                : Staff.Contains(to) ? MessagingRuleModes.Allow : MessagingRuleModes.Deny,
            BuiltInRoles.Counselor or BuiltInRoles.Supervisor => to is BuiltInRoles.Student or BuiltInRoles.Guardian ? MessagingRuleModes.Relationship
                : Staff.Contains(to) ? MessagingRuleModes.Allow : MessagingRuleModes.Deny,
            BuiltInRoles.Manager or BuiltInRoles.OrgAdmin => to == BuiltInRoles.Student ? MessagingRuleModes.Deny : MessagingRuleModes.Allow,
            _ => MessagingRuleModes.Deny,
        };
    }

    /// <summary>The whole default matrix, one cell per (sender, recipient) pair.</summary>
    public static IReadOnlyList<MessagingRuleCellDto> Matrix() =>
        [.. Roles.SelectMany(from => Roles.Select(to => new MessagingRuleCellDto(from, to, Default(from, to))))];

    /// <summary>The cells of a save request that are looser than the defaults, or unknown (empty = the request only narrows).</summary>
    public static IReadOnlyList<MessagingRuleCellDto> Loosened(IEnumerable<MessagingRuleCellDto> cells) =>
        [.. cells.Where(c => MessagingRuleModes.Rank(c.Mode) < 0 || !Roles.Contains(c.FromRole) || !Roles.Contains(c.ToRole)
                             || MessagingRuleModes.Rank(c.Mode) > MessagingRuleModes.Rank(Default(c.FromRole, c.ToRole)))];
}
