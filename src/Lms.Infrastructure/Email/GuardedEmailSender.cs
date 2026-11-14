using Lms.Domain.Abstractions;

namespace Lms.Infrastructure.Email;

/// <summary>
/// Wraps the chosen email transport (D67, D66): a notification email (<see cref="EmailMessage.IsNotification"/>) is checked with
/// <see cref="OutboundContentPolicy"/> before it is handed over, so a grade or an attendance status can never leave through the email channel. Account emails pass
/// unchanged.
/// </summary>
public sealed class GuardedEmailSender(IEmailSender inner) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (message.IsNotification)
        {
            OutboundContentPolicy.EnsureSafe(message.Subject, message.HtmlBody);
        }

        return inner.SendAsync(message, cancellationToken);
    }
}
