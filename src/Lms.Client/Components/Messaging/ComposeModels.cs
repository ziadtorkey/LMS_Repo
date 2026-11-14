namespace Lms.Client.Components.Messaging;

/// <summary>The kind of a row in the recipient picker: a group label, a class or course group, a person, or the D69 safeguarding row.</summary>
public enum RecipientKind
{
    GroupLabel,
    Group,
    Person,
    Safeguarding,
}

/// <summary>
/// One row the server's recipients search returned, already filtered by the messaging rules (spec #615 §3.2). The page maps the M6 contract to these;
/// the picker never decides who may be messaged. A near-miss comes back with <see cref="DisabledReason"/> and is shown but can't be picked.
/// </summary>
public sealed record RecipientChoice(string Id, RecipientKind Kind, string Name, string? Caption = null, string? SizeText = null, string? DisabledReason = null)
{
    public bool Selectable => Kind is RecipientKind.Group or RecipientKind.Person && DisabledReason is null;
}

/// <summary>What the New message dialog edits (spec #615 §3). A UI model only.</summary>
public sealed class MessageDraft
{
    public List<RecipientChoice> To { get; } = [];
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";

    /// <summary>More than one person, or any group: replies go to everyone, so the dialog says so.</summary>
    public bool IsGroup => To.Count > 1 || To.Any(r => r.Kind == RecipientKind.Group);
}

/// <summary>The built-in roles of the messaging rules matrix (spec #615 §7a), as rows ("From") and columns ("To").</summary>
public enum MessagingRole
{
    OrgAdmin,
    Manager,
    Supervisor,
    Counselor,
    Teacher,
    Student,
    Guardian,
}

/// <summary>One cell of the rules matrix: the rule, and whether D69 locks it.</summary>
public sealed class RuleCellValue
{
    public MessagingVia Via { get; set; }
    public bool Locked { get; init; }
}
