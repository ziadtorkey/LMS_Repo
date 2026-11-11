namespace Lms.Domain.Identity;

/// <summary>The account emails: password reset and first-password invitation, in the culture given.</summary>
public interface IAccountEmails
{
    Task SendPasswordResetAsync(User user, string culture, CancellationToken cancellationToken = default);

    Task SendInvitationAsync(User user, string culture, CancellationToken cancellationToken = default);

    /// <summary>The invitation sent to <paramref name="toEmail"/> (an alternate address) instead of the user's own.</summary>
    Task SendInvitationAsync(User user, string culture, string toEmail, CancellationToken cancellationToken);
}
