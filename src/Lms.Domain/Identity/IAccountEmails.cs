namespace Lms.Domain.Identity;

/// <summary>The account emails: password reset and first-password invitation, in the culture given.</summary>
public interface IAccountEmails
{
    Task SendPasswordResetAsync(User user, string culture, CancellationToken cancellationToken = default);

    Task SendInvitationAsync(User user, string culture, CancellationToken cancellationToken = default);

    /// <summary>The student's user name and default password, with the sign-in link and the note that it must be changed at first sign-in.</summary>
    Task SendStudentCredentialsAsync(string userName, string password, string toEmail, string culture, CancellationToken cancellationToken);

    /// <summary>The invitation sent to <paramref name="toEmail"/> (an alternate address) instead of the user's own.</summary>
    Task SendInvitationAsync(User user, string culture, string toEmail, CancellationToken cancellationToken);
}
