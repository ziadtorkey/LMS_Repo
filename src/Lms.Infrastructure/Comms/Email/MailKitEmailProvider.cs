using System.Net.Sockets;
using Lms.Domain.Comms;
using Lms.Infrastructure.Email;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Lms.Infrastructure.Comms.Email;

/// <summary>
/// The email provider over SMTP (D67: MailKit now, a relay chosen before go-live). A delivery problem never throws: it becomes a status with a code
/// (<see cref="SmtpErrorMapper"/>), and nothing about the message, the address or the credentials reaches a log line or a returned value.
/// </summary>
public sealed class MailKitEmailProvider(IOptions<EmailOptions> options, ILogger<MailKitEmailProvider> logger) : IEmailProvider
{
    public async Task<EmailSendResult> SendAsync(EmailEnvelope envelope, CancellationToken cancellationToken)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.SmtpHost))
        {
            return EmailSendResult.NotConfigured();
        }

        try
        {
            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(o.FromName, o.From));
            mime.To.Add(MailboxAddress.Parse(envelope.To));
            mime.Subject = envelope.Subject;
            mime.Body = new BodyBuilder { HtmlBody = envelope.HtmlBody, TextBody = envelope.TextBody }.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(o.SmtpHost, o.SmtpPort, o.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
            if (!string.IsNullOrEmpty(o.UserName))
            {
                await client.AuthenticateAsync(o.UserName, o.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
            return EmailSendResult.Accepted(mime.MessageId);
        }
        catch (OperationCanceledException)
        {
            throw; // the dispatcher is shutting down: the lease returns the row to Pending
        }
        catch (Exception ex)
        {
            var result = SmtpErrorMapper.Map(ex);
            // Type and code only: the exception message can carry the address or the relay's echo of the body.
            logger.LogWarning("Email send failed: {Status} {Code} ({ExceptionType})", result.Status, result.ErrorCode, ex.GetType().Name);
            return result;
        }
    }
}

/// <summary>Maps what SMTP threw to a status and a short code (never a message): 5xx refuses for good, 4xx and network trouble retry, bad credentials need setup.</summary>
public static class SmtpErrorMapper
{
    public static EmailSendResult Map(Exception exception) => exception switch
    {
        AuthenticationException => EmailSendResult.NotConfigured() with { ErrorCode = "smtp_auth" },
        SmtpCommandException { StatusCode: >= (SmtpStatusCode)500 } => EmailSendResult.Permanent("smtp_5xx"),
        SmtpCommandException { StatusCode: >= (SmtpStatusCode)400 } => EmailSendResult.Transient("smtp_4xx"),
        SmtpCommandException => EmailSendResult.Transient("smtp_error"),
        FormatException or ParseException => EmailSendResult.Permanent("address_invalid"),
        SslHandshakeException => EmailSendResult.NotConfigured() with { ErrorCode = "smtp_tls" },
        SocketException or IOException or ServiceNotConnectedException or ServiceNotAuthenticatedException or TimeoutException =>
            EmailSendResult.Transient("network"),
        ProtocolException => EmailSendResult.Transient("smtp_protocol"),
        _ => EmailSendResult.Transient("unknown"),
    };
}
