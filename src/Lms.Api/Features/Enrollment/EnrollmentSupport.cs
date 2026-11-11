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
            extensions: Extensions(rule));

    private static Dictionary<string, object?> Extensions(EnrollmentRuleException rule)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = rule.Code };
        if (rule.FailedTypes.Count > 0)
        {
            extensions["failedTypes"] = rule.FailedTypes;
        }

        if (rule.Reason is not null)
        {
            extensions["reason"] = rule.Reason;
        }

        return extensions;
    }

    public static IResult Invalid(string field, string key) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { [field] = [key] },
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });
}
