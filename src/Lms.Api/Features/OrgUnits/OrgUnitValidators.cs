using System.Text.RegularExpressions;
using Lms.Shared;
using Lms.Shared.Organizations;

namespace Lms.Api.Features.OrgUnits;

/// <summary>Errors map a field to short keys (required, too_long, invalid_format, invalid_value); code validation_failed.</summary>
public static partial class OrgUnitValidators
{
    public const int MaxNameLength = 200;

    public static IResult ToProblem(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(
            errors, extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });

    public static Dictionary<string, string[]> Validate(CreateOrgUnitRequest r)
    {
        var errors = Fields(r.NameAr, r.NameEn, r.Code);
        if (r.ParentId == Guid.Empty)
        {
            errors[nameof(r.ParentId)] = ["required"];
        }

        if (r.LevelTypeId == Guid.Empty)
        {
            errors[nameof(r.LevelTypeId)] = ["required"];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(UpdateOrgUnitRequest r)
    {
        var errors = Fields(r.NameAr, r.NameEn, r.Code);
        if (r.LevelTypeId == Guid.Empty)
        {
            errors[nameof(r.LevelTypeId)] = ["required"];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(SetOrgUnitStatusRequest r) =>
        OrgUnitStatuses.All.Contains(r.Status ?? string.Empty)
            ? []
            : new Dictionary<string, string[]> { [nameof(r.Status)] = ["invalid_value"] };

    private static Dictionary<string, string[]> Fields(string? nameAr, string? nameEn, string? code)
    {
        var errors = new Dictionary<string, string[]>();
        Name(errors, "NameAr", nameAr);
        Name(errors, "NameEn", nameEn);
        if (string.IsNullOrWhiteSpace(code))
        {
            errors["Code"] = ["required"];
        }
        else if (!UnitCode().IsMatch(code))
        {
            errors["Code"] = ["invalid_format"];
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

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,50}\z")]
    private static partial Regex UnitCode();
}