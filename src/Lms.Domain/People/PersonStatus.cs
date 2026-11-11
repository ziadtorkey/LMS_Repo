namespace Lms.Domain.People;

/// <summary>Lifecycle of a person's profile. Stored as its name; the gate on role assignments reads it (M2 Task 6).</summary>
public enum PersonStatus
{
    Active,
    Deactivated,
    Expired,
}
