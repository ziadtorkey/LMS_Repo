namespace Lms.Domain.People;

/// <summary>The kind of profile a person has in a client: one profile per kind (a person can be staff and guardian).</summary>
public enum PersonKind
{
    Staff,
    Student,
    Guardian,
}
