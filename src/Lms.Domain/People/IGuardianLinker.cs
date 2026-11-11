namespace Lms.Domain.People;

/// <summary>The editable data of a guardian, as given by the caller (normalized and validated by the linker).</summary>
public sealed record GuardianData(string NameAr, string NameEn, string Mobile, string? Email, string? NationalId);

/// <summary>A guardian to link: exactly one of <c>ExistingGuardianId</c> and <c>New</c>.</summary>
public sealed record GuardianLink(Guid? ExistingGuardianId, GuardianData? New, string Relation, bool IsPrimary);

/// <summary>A guardian operation that cannot be done; <c>Code</c> is a problem code, <c>Field</c> names the offending input when there is one.</summary>
public sealed class GuardianLinkException(string code, string? field = null) : Exception(code)
{
    public string Code { get; } = code;

    public string? Field { get; } = field;
}

/// <summary>
/// Called after a student's guardian links changed and were saved (link, unlink, primary change, guardian data edit).
/// Portal-account sync registers here (M3 Task 25) instead of editing the linker.
/// </summary>
public interface IGuardianLinkObserver
{
    Task OnLinksChangedAsync(GuardianProfile guardian, CancellationToken cancellationToken);
}

/// <summary>
/// Guardians of a student (FR-STU-06): one guardian for all siblings, several guardians with exactly one primary, and a
/// per-unit setting that makes at least one guardian mandatory. Each method saves its own changes, then notifies the
/// observers.
/// </summary>
public interface IGuardianLinker
{
    /// <summary>
    /// Links the guardians to the student. A new guardian that matches an existing one of the client (national id first, then
    /// mobile) is reused (siblings). Afterwards the student has exactly one primary guardian: the first input marked primary, else
    /// the one that was already primary, else the oldest link.
    /// </summary>
    Task<IReadOnlyList<StudentGuardian>> LinkAsync(StudentProfile student, IReadOnlyList<GuardianLink> inputs, CancellationToken cancellationToken);

    /// <summary>Changes relation and primary flag of one link; unsetting the only primary is <c>primary_guardian_required</c>.</summary>
    Task<StudentGuardian> UpdateLinkAsync(StudentProfile student, Guid guardianId, string relation, bool isPrimary, CancellationToken cancellationToken);

    /// <summary>Removes one link (the guardian profile stays). The last guardian cannot go while the unit requires one; a removed primary hands over to the oldest remaining link.</summary>
    Task UnlinkAsync(StudentProfile student, Guid guardianId, CancellationToken cancellationToken);

    /// <summary><c>Guardians.DataRequired</c> resolved at the student's home unit: required and no guardian is <c>guardian_required</c>.</summary>
    Task EnsureRequirementAsync(StudentProfile student, int guardianCount, CancellationToken cancellationToken);

    /// <summary>Edits the data of a guardian; the national id or mobile of another guardian of the client is <c>duplicate_person</c> (field).</summary>
    Task<GuardianProfile> UpdateGuardianAsync(Guid guardianId, GuardianData data, CancellationToken cancellationToken);
}
