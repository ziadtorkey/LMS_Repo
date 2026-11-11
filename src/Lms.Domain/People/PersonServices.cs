using Lms.Domain.Identity;
using Lms.Domain.Organizations;

namespace Lms.Domain.People;

/// <summary>Finds or creates the platform-level user behind a person.</summary>
public interface IPersonAccounts
{
    /// <summary>
    /// The user with the (normalized) email, else the one with the user name; created without a password when neither
    /// exists. Uses the request's DbContext, so it joins the caller's transaction.
    /// </summary>
    Task<(User User, bool Created)> EnsureUserAsync(
        string? email, string userName, string nameAr, string nameEn, CancellationToken cancellationToken);
}

/// <summary>Collects invitations during a handler and sends them after the handler's transaction committed.</summary>
public interface IInvitationOutbox
{
    /// <param name="toEmail">Send here instead of the user's own email (an alternate address); null = the user's email.</param>
    void Enqueue(Guid userId, string culture, string? toEmail = null);

    /// <summary>Sends the queued invitations to users that still have no password. A failed send is logged, never thrown.</summary>
    Task FlushAsync(CancellationToken cancellationToken);
}

/// <summary>Status changes of profiles, with the history row and the user's derived status.</summary>
public interface IPersonStatusService
{
    /// <summary>Writes the history row, sets the profile status, saves, then recomputes the user's status. Staff and student only.</summary>
    Task ChangeAsync(PersonKind kind, Guid profileId, PersonStatus to, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Derives <c>User.Status</c> from the user's profiles in every client: any active profile (or none at all) means
    /// Active; otherwise Expired when every inactive profile is expired, else Deactivated. Platform admins and
    /// withdrawn users are left alone.
    /// </summary>
    Task RecomputeUserStatusAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Keeps one assignment of a built-in role per unit (student and guardian roles).</summary>
public interface IRoleAssignmentSync
{
    /// <summary>
    /// The user's live assignments of the built-in role in the client become exactly one per given unit: missing ones are
    /// added, extra ones soft-deleted. Saves.
    /// </summary>
    Task SyncBuiltInAsync(
        Guid userId, Guid organizationId, string builtInRoleKey, IReadOnlyCollection<OrgUnit> units, CancellationToken cancellationToken);
}

/// <summary>
/// The student's email or sign-in name already belongs to another account. Reusing it would let the caller set (or reset) the
/// password of someone else's account, so it is refused: <c>duplicate_person</c>, field <c>Field</c>.
/// </summary>
public sealed class DuplicateAccountException(string field) : Exception("The account belongs to a person outside this client.")
{
    public string Field { get; } = field;
}

/// <summary>What a student account was issued with (D24). <c>Password</c> is null when an existing active account was left alone.</summary>
public sealed record IssuedCredentials(User User, string? Password, string DeliveredTo);

/// <summary>The requested default password was refused (the Identity password policy); <c>Errors</c> are the policy error codes.</summary>
public sealed class StudentCredentialsException(IReadOnlyList<string> errors) : Exception("The password does not meet the policy.")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// The login account of a student (D24, D45): user name = the lower-case student code, a default password (the requested one, or a
/// generated strong one) that must be changed at first sign-in, and the delivery of the credentials after the caller's commit.
/// The clear password exists only in the returned value and in the queued delivery; it is never logged, stored or audited.
/// </summary>
public interface IStudentCredentials
{
    /// <summary>
    /// Ensures the account and sets the default password with <c>MustChangePassword</c>, links <c>StudentProfile.UserId</c> (the caller
    /// saves) and queues the email: to the student when the profile has an email, else to the primary guardian when that one has,
    /// else nothing (<c>DeliveredTo</c> = <c>None</c>). An existing account that already has a password is left alone unless
    /// <paramref name="reset"/>. A requested password that fails the policy throws <see cref="StudentCredentialsException"/> before anything is created.
    /// </summary>
    Task<IssuedCredentials> IssueAsync(StudentProfile student, string? requestedPassword, bool reset, CancellationToken cancellationToken);

    /// <summary>Sends the queued credentials emails. Call after the transaction committed; a failed send is logged (never with the password), never thrown.</summary>
    Task FlushDeliveriesAsync(CancellationToken cancellationToken);
}
