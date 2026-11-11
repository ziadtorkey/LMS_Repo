namespace Lms.Domain.People;

/// <summary>
/// Client-wide duplicate lookups. They deliberately look beyond the caller's unit scope (a person registered in a
/// sibling school is still a duplicate) and therefore reveal only which field collided, never the other record.
/// All arguments are in <see cref="PersonNormalizer"/> form; only live (not deleted) rows count.
/// </summary>
public interface IPersonDuplicateFinder
{
    /// <summary>"email", "mobile" or "nationalId" for the first staff field that collides within the client; null when none.</summary>
    Task<string?> FindStaffConflictAsync(
        Guid organizationId, string email, string? mobile, string? nationalId, Guid? excludeProfileId, CancellationToken ct);

    Task<bool> StudentNationalIdExistsAsync(Guid organizationId, string nationalId, Guid? excludeId, CancellationToken ct);

    /// <summary>Students with the same date of birth whose Arabic or English name key equals the given one.</summary>
    Task<int> CountStudentsByNameAndDobAsync(
        Guid organizationId, string nameArKey, string nameEnKey, DateOnly dob, Guid? excludeId, CancellationToken ct);

    /// <summary>The guardian with the national id, else the one with the mobile number; null when none.</summary>
    Task<GuardianProfile?> FindGuardianAsync(Guid organizationId, string? nationalId, string? mobile, CancellationToken ct);
}
