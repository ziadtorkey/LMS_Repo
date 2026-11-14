namespace Lms.Domain.Abstractions;

/// <param name="IsNotification">
/// True for a notification email (M6): its subject and body must stay generic, so the sender checks them with <see cref="OutboundContentPolicy"/> before anything is sent.
/// Account emails (a password link, a welcome) leave it false.
/// </param>
public sealed record EmailMessage(string To, string Subject, string HtmlBody, bool IsNotification = false);

/// <summary>Sends one email. SMTP today; a queued notification channel replaces it in M6.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
