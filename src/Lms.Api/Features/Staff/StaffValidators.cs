using System.Net.Mail;
using Lms.Domain.People;
using Lms.Shared;

namespace Lms.Api.Features.Staff;

/// <summary>The normalized identity values of a staff request (PersonNormalizer form).</summary>
internal sealed record StaffIdentity(string? Email, string? Mobile, string? NationalId);

/// <summary>Field rules of the staff create/edit bodies (M2 Global Constraints: lengths, normalization, expiry).</summary>
internal static class StaffValidators
{
    private const int MaxName = 200;
    private const int MaxJobTitle = 150;
    private const int MaxEmail = 256;

    public static IResult? Validate(
        string? nameAr, string? nameEn, string? email, bool emailRequired, string? mobile, string? nationalId,
        string? jobTitleAr, string? jobTitleEn, DateOnly? expiresOn, DateOnly today, out StaffIdentity identity)
    {
        identity = new StaffIdentity(null, null, null);
        var errors = new Dictionary<string, string[]>();
        Require(errors, "NameAr", nameAr, MaxName);
        Require(errors, "NameEn", nameEn, MaxName);
        var normalizedEmail = PersonNormalizer.Email(email);
        if (emailRequired
            && (normalizedEmail is null || normalizedEmail.Length > MaxEmail
                || !MailAddress.TryCreate(normalizedEmail, out var parsed) || parsed.Address != normalizedEmail))
        {
            errors["Email"] = ["email_invalid"];
        }

        var normalizedMobile = PersonNormalizer.Mobile(mobile);
        if (!string.IsNullOrWhiteSpace(mobile) && normalizedMobile is null)
        {
            errors["Mobile"] = ["mobile_invalid"];
        }

        var normalizedNationalId = PersonNormalizer.NationalId(nationalId);
        if (!string.IsNullOrWhiteSpace(nationalId) && normalizedNationalId is null)
        {
            errors["NationalId"] = ["national_id_invalid"];
        }

        if (jobTitleAr?.Trim().Length > MaxJobTitle)
        {
            errors["JobTitleAr"] = ["too_long"];
        }

        if (jobTitleEn?.Trim().Length > MaxJobTitle)
        {
            errors["JobTitleEn"] = ["too_long"];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors, extensions: Code(ProblemCodes.validation_failed));
        }

        if (expiresOn is { } expiry && expiry < today)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "Bad request", extensions: Code(ProblemCodes.invalid_expiry));
        }

        identity = new StaffIdentity(normalizedEmail, normalizedMobile, normalizedNationalId);
        return null;
    }

    /// <summary>Trims an optional text; empty becomes null.</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Require(Dictionary<string, string[]> errors, string field, string? value, int max)
    {
        var length = value?.Trim().Length ?? 0;
        if (length is 0)
        {
            errors[field] = ["required"];
        }
        else if (length > max)
        {
            errors[field] = ["too_long"];
        }
    }

    private static Dictionary<string, object?> Code(string code) => new() { ["code"] = code };
}
