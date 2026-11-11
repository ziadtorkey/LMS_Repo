using System.Net.Mail;
using System.Text.RegularExpressions;
using Lms.Shared;
using Lms.Shared.Organizations;

namespace Lms.Api.Features.Clients;

/// <summary>
/// Validation for the client endpoints. Errors map a field to short machine keys (required, too_long, invalid_format,
/// invalid_value) that the client turns into localized messages; the response carries code validation_failed.
/// </summary>
public static partial class ClientValidators
{
    public const int MaxNameLength = 200;

    public static IResult ToProblem(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(
            errors, extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });

    public static Dictionary<string, string[]> Validate(CreateClientRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        Name(errors, nameof(r.NameAr), r.NameAr);
        Name(errors, nameof(r.NameEn), r.NameEn);
        Name(errors, nameof(r.RootLevelNameAr), r.RootLevelNameAr);
        Name(errors, nameof(r.RootLevelNameEn), r.RootLevelNameEn);
        Name(errors, nameof(r.AdminDisplayNameAr), r.AdminDisplayNameAr);
        Name(errors, nameof(r.AdminDisplayNameEn), r.AdminDisplayNameEn);

        if (!OrganizationTypes.All.Contains(r.Type ?? string.Empty))
        {
            errors[nameof(r.Type)] = ["invalid_value"];
        }

        if (string.IsNullOrWhiteSpace(r.RootUnitCode))
        {
            errors[nameof(r.RootUnitCode)] = ["required"];
        }
        else if (!UnitCode().IsMatch(r.RootUnitCode))
        {
            errors[nameof(r.RootUnitCode)] = ["invalid_format"];
        }

        if (string.IsNullOrWhiteSpace(r.AdminEmail))
        {
            errors[nameof(r.AdminEmail)] = ["required"];
        }
        else if (!IsEmail(r.AdminEmail.Trim()))
        {
            errors[nameof(r.AdminEmail)] = ["invalid_format"];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(UpdateClientRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        Name(errors, nameof(r.NameAr), r.NameAr);
        Name(errors, nameof(r.NameEn), r.NameEn);
        if (!OrganizationStatuses.All.Contains(r.Status ?? string.Empty))
        {
            errors[nameof(r.Status)] = ["invalid_value"];
        }

        return errors;
    }

    /// <summary>Paging rules (M1 Global Constraints): page at least 1, pageSize 1..200, default 50.</summary>
    public static Dictionary<string, string[]> ValidatePaging(int page, int pageSize)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1)
        {
            errors["page"] = ["invalid_value"];
        }

        if (pageSize is < 1 or > Paging.MaxPageSize)
        {
            errors["pageSize"] = ["invalid_value"];
        }

        return errors;
    }

    private static void Name(Dictionary<string, string[]> errors, string field, string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            errors[field] = ["required"];
        }
        else if (trimmed.Length > MaxNameLength)
        {
            errors[field] = ["too_long"];
        }
    }

    private static bool IsEmail(string value) =>
        value.Length <= 256 && MailAddress.TryCreate(value, out var address) && address.Address == value && value.Contains('@');

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,50}\z")]
    private static partial Regex UnitCode();
}