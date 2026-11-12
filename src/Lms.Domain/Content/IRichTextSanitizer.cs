namespace Lms.Domain.Content;

/// <summary>
/// Sanitises rich-text HTML on write (D48): page bodies, assignment instructions, forum posts and announcements.
/// One shared allow-list; the client only ever renders what this returned.
/// </summary>
public interface IRichTextSanitizer
{
    /// <summary>The longest body accepted, in characters of the raw input.</summary>
    const int MaxLength = 200_000;

    /// <summary>
    /// Returns the sanitised HTML; null or blank input gives an empty string.
    /// </summary>
    /// <exception cref="RichTextTooLongException">The input is longer than <see cref="MaxLength"/>.</exception>
    string Sanitize(string? html);
}

/// <summary>A rich-text body is longer than <see cref="IRichTextSanitizer.MaxLength"/> characters.</summary>
public sealed class RichTextTooLongException(int length)
    : InvalidOperationException($"The text is {length} characters; the maximum is {IRichTextSanitizer.MaxLength}.")
{
    public int Length { get; } = length;
}
