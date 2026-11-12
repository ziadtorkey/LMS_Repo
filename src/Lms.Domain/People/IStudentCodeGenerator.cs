using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Domain.People;

/// <summary>
/// The running student-code sequence of a client (D25): one row per client, never reset (not per school, not per year,
/// not when the pattern changes). Holds the last value issued. Not audited.
/// </summary>
public sealed class StudentCodeCounter : ITenantOwned
{
    private StudentCodeCounter()
    {
    }

    public Guid OrganizationId { get; private set; }

    public long NextValue { get; private set; }
}

/// <summary>The client's sequence ran past the digits of its pattern (409 <c>student_code_exhausted</c>).</summary>
public sealed class StudentCodeExhaustedException(string message) : Exception(message);

/// <summary>The pattern renders a code that is not a valid student code (for example a root code with an underscore).</summary>
public sealed class StudentCodeInvalidException(string message) : Exception(message);

public interface IStudentCodeGenerator
{
    /// <summary>
    /// The next free code for a student at <paramref name="homeUnit"/>: the pattern is resolved at the unit, the number
    /// comes from the client-wide counter inside the caller's transaction (a rollback gives the number back), and a code
    /// already taken (manual entries, deleted students included) is skipped. At most 100 tries.
    /// </summary>
    /// <exception cref="StudentCodeExhaustedException">The sequence no longer fits the pattern's digits.</exception>
    Task<string> GenerateAsync(OrgUnit homeUnit, CancellationToken cancellationToken);

    /// <summary>
    /// A problem code when a manually entered code cannot be used: <c>validation_failed</c> (not 1-30 letters, digits or
    /// hyphens) or <c>student_code_taken</c> (any student of the client, deleted ones included, except
    /// <paramref name="excludeId"/>); null when it is fine.
    /// </summary>
    Task<string?> ValidateManualAsync(Guid organizationId, string code, Guid? excludeId, CancellationToken cancellationToken);
}
