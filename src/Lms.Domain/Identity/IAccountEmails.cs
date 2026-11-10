namespace Lms.Domain.Identity;

/// <summary>The account emails: password reset and first-password invitation, in the culture given.</summary>
public interface IAccountEmails
{
    Task SendPasswordResetAsync(User user, string culture, CancellationToken cancellationToken = default);

    Task SendInvitationAsync(User user, string culture, CancellationToken cancellationToken = default);
}
