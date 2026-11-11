using Lms.Domain.Identity;
using Lms.Domain.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.People;

/// <summary>Scoped queue of invitations; handlers flush it after their transaction committed (nothing is sent for a rollback).</summary>
public sealed class InvitationOutbox(UserManager<User> users, IAccountEmails emails, ILogger<InvitationOutbox> logger)
    : IInvitationOutbox
{
    private readonly List<(Guid UserId, string Culture, string? ToEmail)> _queue = [];

    public void Enqueue(Guid userId, string culture, string? toEmail = null) => _queue.Add((userId, culture, toEmail));

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var pending = _queue.ToArray();
        _queue.Clear();
        foreach (var (userId, culture, toEmail) in pending)
        {
            try
            {
                var user = await users.FindByIdAsync(userId.ToString());
                if (user is null || user.PasswordHash is not null || (toEmail ?? user.Email) is not { Length: > 0 } address)
                {
                    continue; // already activated, gone, or nowhere to send it
                }

                await emails.SendInvitationAsync(user, culture, address, cancellationToken);
            }
            catch (Exception ex)
            {
                // The account exists; a mail outage must not undo it. An admin can resend the invitation.
                logger.LogWarning(ex, "Invitation email to user {UserId} could not be sent", userId);
            }
        }
    }
}
