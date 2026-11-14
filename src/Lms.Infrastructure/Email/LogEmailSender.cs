using Lms.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Email;

/// <summary>
/// The <c>Log</c> provider (D67): sends nothing and writes one log line with the recipient masked and the lengths of the subject and body, never their text (they can
/// hold a one-time link). For tests and for environments that must not send mail.
/// </summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Email not sent (Email:Provider=Log): to {Recipient}, subject {SubjectLength} characters, body {BodyLength} characters",
            Masking.Email(message.To), message.Subject.Length, message.HtmlBody.Length);
        return Task.CompletedTask;
    }
}

/// <summary>Masks contact details for logs: the first character and the domain of an email, the last two digits of a phone number.</summary>
public static class Masking
{
    public static string Email(string? address)
    {
        var at = address?.IndexOf('@') ?? -1;
        return at > 0 ? $"{address![0]}***{address[at..]}" : "***";
    }

    public static string Phone(string? number) =>
        string.IsNullOrWhiteSpace(number) || number.Length < 4 ? "***" : $"***{number[^2..]}";
}
