using Lms.Shared;
using Lms.Shared.Organizations;

namespace Lms.Api.Features.LevelTypes;

/// <summary>Request validation. Error values are message keys the client translates (never display text).</summary>
public static class LevelTypeValidators
{
    public const int MaxNameLength = 200;

    /// <summary>The request with trimmed names, or the validation problem to return.</summary>
    public static (SaveLevelTypeRequest? Request, IResult? Problem) Validate(SaveLevelTypeRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        var nameAr = request?.NameAr?.Trim() ?? string.Empty;
        var nameEn = request?.NameEn?.Trim() ?? string.Empty;
        CheckName(errors, "nameAr", nameAr);
        CheckName(errors, "nameEn", nameEn);

        return errors.Count == 0
            ? (new SaveLevelTypeRequest(nameAr, nameEn, request!.CanHostClasses), null)
            : (null, ValidationFailed(errors));
    }

    public static IResult ValidationFailed(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(errors, extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });

    private static void CheckName(Dictionary<string, string[]> errors, string field, string value)
    {
        if (value.Length == 0)
        {
            errors[field] = ["name_required"];
        }
        else if (value.Length > MaxNameLength)
        {
            errors[field] = ["name_too_long"];
        }
    }
}
