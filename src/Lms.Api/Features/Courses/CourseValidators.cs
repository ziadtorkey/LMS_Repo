using System.Text.RegularExpressions;
using Lms.Api.Infrastructure;
using Lms.Shared;
using Lms.Shared.Courses;

namespace Lms.Api.Features.Courses;

/// <summary>Request validation. Failures are 400 <c>validation_failed</c>; the detail names the offending field.</summary>
public static partial class CourseValidators
{
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 2000;
    public const int MaxWeeklyPeriods = 40;

    [GeneratedRegex("^[A-Za-z0-9_-]{1,50}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[a-z]{2}$")]
    private static partial Regex LanguagePattern();

    public static IResult? Validate(SaveCourseRequest request)
    {
        if (request.Code is null || !CodePattern().IsMatch(request.Code.Trim()))
        {
            return Invalid("Code must be 1-50 letters, digits, '-' or '_'.");
        }

        if (string.IsNullOrWhiteSpace(request.NameAr) || request.NameAr.Trim().Length > MaxNameLength)
        {
            return Invalid("NameAr is required (max 200 characters).");
        }

        if (string.IsNullOrWhiteSpace(request.NameEn) || request.NameEn.Trim().Length > MaxNameLength)
        {
            return Invalid("NameEn is required (max 200 characters).");
        }

        if (request.DescriptionAr is { Length: > MaxDescriptionLength } || request.DescriptionEn is { Length: > MaxDescriptionLength })
        {
            return Invalid("Descriptions are at most 2000 characters.");
        }

        if (request.Language is null || !LanguagePattern().IsMatch(request.Language))
        {
            return Invalid("Language must be a two-letter ISO 639-1 code.");
        }

        if (request.WeeklyPeriods is < 0 or > MaxWeeklyPeriods)
        {
            return Invalid("WeeklyPeriods must be between 0 and 40.");
        }

        return null;
    }

    public static IResult? Validate(SetCourseStatusRequest request) =>
        request.Status is not null && CourseStatuses.All.Contains(request.Status) ? null : Invalid("Status is not valid.");

    /// <summary>Codes are stored upper-case.</summary>
    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private static IResult Invalid(string detail) => ApiProblems.BadRequest(ProblemCodes.validation_failed, detail);
}
