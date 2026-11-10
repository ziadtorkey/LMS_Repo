namespace Lms.Domain.Abstractions;

public sealed record EmailMessage(string To, string Subject, string HtmlBody);

/// <summary>Sends one email. SMTP today; a queued notification channel replaces it in M6.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
