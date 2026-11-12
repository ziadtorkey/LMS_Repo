namespace Lms.Domain.Content;

/// <summary>
/// The one HTML allow-list of every rich text written through the API (D48): page bodies, assignment instructions, forum
/// posts and announcements are sanitised on write, never on read.
/// </summary>
public interface IRichTextSanitizer
{
    /// <summary>The sanitised HTML, or null for null / blank input.</summary>
    string? Sanitize(string? html);
}
