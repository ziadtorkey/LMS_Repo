namespace Lms.Infrastructure.Email;

/// <summary>Bound from the Email configuration section (Mailpit on localhost:1025 in development).</summary>
public sealed class EmailOptions
{
    /// <summary>
    /// <c>Smtp</c> (default; Mailpit locally, the relay chosen before go-live in production) or <c>Log</c> (sends nothing). Any other value stops the startup:
    /// the concrete relay is picked before go-live and gets its own adapter (D67).
    /// </summary>
    public string Provider { get; set; } = "Smtp";

    public string SmtpHost { get; set; } = "localhost";
    public int SmtpPort { get; set; } = 1025;
    public string From { get; set; } = "no-reply@lms.local";
    public string FromName { get; set; } = "LMS";

    /// <summary>Negotiate STARTTLS (off for the local Mailpit).</summary>
    public bool UseStartTls { get; set; }

    public string? UserName { get; set; }
    public string? Password { get; set; }
}
