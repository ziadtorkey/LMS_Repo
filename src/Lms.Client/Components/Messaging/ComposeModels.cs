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

/// <summary>
/// One cell of the rules matrix. A school may only narrow a rule, never widen it (D69: the Org Admin may tighten per client): the server's default
/// for the cell is its <see cref="Ceiling"/>, and a value wider than it is never offered or accepted. A cell D69 fixes is <see cref="Locked"/>.
/// </summary>
public sealed class RuleCellValue
{
    public MessagingVia Via { get; set; }

    /// <summary>The widest value this cell may take: the platform default (D69 floor). Set by the server; defaults to the initial value.</summary>
    public MessagingVia? Ceiling { get; init; }
    public bool Locked { get; init; }

    /// <summary>Set by the page when the server refused this cell (400 rules_looser_than_default): shown on the cell, in text.</summary>
    public string? Error { get; set; }

    public MessagingVia Max => Ceiling ?? Via;

    /// <summary>Narrow to wide: Not allowed, Own classes, Same unit, Anyone. Groups only is a locked D69 value.</summary>
    public static int Rank(MessagingVia via) => via switch
    {
        MessagingVia.NotAllowed => 0,
        MessagingVia.Relationship => 1,
        MessagingVia.SameUnit => 2,
        MessagingVia.Anywhere => 3,
        _ => -1,
    };

    /// <summary>Whether this cell may take <paramref name="via"/>: never on a locked cell, never wider than the ceiling.</summary>
    public bool Allows(MessagingVia via) => !Locked && Rank(via) >= 0 && Rank(via) <= Rank(Max);
}
