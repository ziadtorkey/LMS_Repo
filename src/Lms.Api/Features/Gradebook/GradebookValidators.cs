using Lms.Domain.Grading;
using Lms.Shared;
using Lms.Shared.Grading;

namespace Lms.Api.Features.Gradebook;

/// <summary>Field rules of the gradebook bodies and the mapping of a <see cref="GradebookRuleException"/> to a problem (M5 Task 8).</summary>
internal static class GradebookValidators
{
    public const int MaxChanges = 200;

    public static IResult Invalid(string field, string key) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { [field] = [key] },
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });

    /// <summary>404 stays the uniform not-found; every other rule keeps its status and code.</summary>
    public static IResult From(GradebookRuleException rule, object? extra = null)
    {
        if (rule.StatusCode == StatusCodes.Status404NotFound)
        {
            return Infrastructure.ApiProblems.NotFound();
        }

        var extensions = new Dictionary<string, object?> { ["code"] = rule.Code };
        if (extra is not null)
        {
            extensions["extra"] = extra;
        }

        return Results.Problem(statusCode: rule.StatusCode, title: rule.StatusCode == 409 ? "Conflict" : "Bad request", extensions: extensions);
    }

    public static IResult? Name(string? ar, string? en, string field) =>
        string.IsNullOrWhiteSpace(ar) || string.IsNullOrWhiteSpace(en) ? Invalid(field, "required")
        : ar.Trim().Length > GradingLimits.NameMaxLength || en.Trim().Length > GradingLimits.NameMaxLength ? Invalid(field, "too_long")
        : null;

    public static IResult? ValidateCategories(SaveGradeCategoriesRequest? request)
    {
        if (request?.Categories is null || request.Categories.Count > 50)
        {
            return Invalid("Categories", "required");
        }

        return request.Categories.Select(c => c is null ? Invalid("Categories", "required") : Name(c.NameAr, c.NameEn, "Categories"))
            .FirstOrDefault(p => p is not null);
    }

    public static IResult? ValidateItem(string? titleAr, string? titleEn, decimal maxPoints) =>
        Name(titleAr, titleEn, "Title")
        ?? (maxPoints is < GradingLimits.MaxPointsMin or > GradingLimits.MaxPointsMax ? Invalid("MaxPoints", "out_of_range") : null);

    public static IResult? ValidateEntries(UpsertEntriesRequest? request) =>
        request?.Changes is null || request.Changes.Count == 0 || request.Changes.Count > MaxChanges || request.Changes.Any(c => c is null)
            ? Invalid("Changes", "required")
            : request.Version < 0 ? Invalid("Version", "out_of_range") : null;
}
