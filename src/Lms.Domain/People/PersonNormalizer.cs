using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lms.Domain.People;

/// <summary>
/// The stored form of the identifying data of a person (M2 Global Constraints). Duplicate checks and unique indexes
/// compare these forms, so every writer and every lookup must go through this class.
/// </summary>
public static partial class PersonNormalizer
{
    /// <summary>Trimmed, lower-case email; null for a missing one. Validity is the caller's concern (MailAddress).</summary>
    public static string? Email(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    /// <summary>Digits with an optional leading '+'; spaces, dashes and parentheses removed. Null when missing or invalid.</summary>
    public static string? Mobile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = new StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            if (c is ' ' or '-' or '(' or ')' or ' ')
            {
                continue;
            }

            cleaned.Append(c);
        }

        var result = cleaned.ToString();
        return MobilePattern().IsMatch(result) ? result : null;
    }

    public static bool IsValidMobile(string? value) => Mobile(value) is not null;

    /// <summary>Trimmed, upper-case national id of 5-20 letters and digits. Null when missing or invalid.</summary>
    public static string? NationalId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var result = value.Trim().ToUpperInvariant();
        return NationalIdPattern().IsMatch(result) ? result : null;
    }

    public static bool IsValidNationalId(string? value) => NationalId(value) is not null;

    /// <summary>
    /// Search/duplicate key of a name: trimmed, whitespace collapsed, lower-case, Arabic diacritics (U+064B-U+0652) and
    /// tatweel (U+0640) removed, alef forms to U+0627, alef maqsura U+0649 to ya U+064A, ta marbuta U+0629 to ha U+0647.
    /// </summary>
    public static string NameKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        var pendingSpace = false;
        foreach (var raw in name.Trim().ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsWhiteSpace(raw))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (raw is >= 'ً' and <= 'ْ' or 'ـ')
            {
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(raw switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                _ => raw,
            });
        }

        return builder.ToString();
    }

    [GeneratedRegex("^\\+?[0-9]{8,15}$")]
    private static partial Regex MobilePattern();

    [GeneratedRegex("^[A-Za-z0-9]{5,20}$")]
    private static partial Regex NationalIdPattern();
}
