namespace Lms.Domain.Comms;

/// <summary>The text of an email template in one language ("ar" or "en"); null when the key does not exist. Backed by resources, never by code (X-05).</summary>
public interface IEmailTemplateText
{
    string? Get(string key, string language);
}
