using Lms.Domain.Enrollment;
using Lms.Shared;

namespace Lms.Api.Features.Enrollment;

/// <summary>Answers of the enrollment endpoints for the rule failures of the engine.</summary>
internal static class EnrollmentProblems
{
    public static IResult From(EnrollmentRuleException rule) =>
        Results.Problem(
            statusCode: rule.StatusCode,
            title: rule.StatusCode == StatusCodes.Status400BadRequest ? "Bad request" : "Conflict",
            extensions: rule.FailedTypes.Count == 0
                ? new Dictionary<string, object?> { ["code"] = rule.Code }
                : new Dictionary<string, object?> { ["code"] = rule.Code, ["failedTypes"] = rule.FailedTypes });

    public static IResult Invalid(string field, string key) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { [field] = [key] },
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });
}
