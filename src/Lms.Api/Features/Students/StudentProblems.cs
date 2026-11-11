using Lms.Api.Features.Guardians;
using Lms.Api.Infrastructure;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Shared;

namespace Lms.Api.Features.Students;

/// <summary>Maps the refusals of the student services to problem responses.</summary>
internal static class StudentProblems
{
    public static IResult? From(Exception ex) => ex switch
    {
        EnrollmentRuleException e => Results.Problem(
            statusCode: e.StatusCode, title: e.StatusCode == StatusCodes.Status400BadRequest ? "Bad request" : "Conflict",
            extensions: new Dictionary<string, object?> { ["code"] = e.Code }),
        GuardianLinkException e => GuardianValidators.ToProblem(e),
        StudentCredentialsException e => Results.ValidationProblem(
            new Dictionary<string, string[]> { ["DefaultPassword"] = [.. e.Errors] },
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed }),
        StudentCodeExhaustedException => ApiProblems.Conflict(ProblemCodes.student_code_exhausted),
        StudentCodeInvalidException => ApiProblems.BadRequest(ProblemCodes.invalid_code_pattern),
        _ => null,
    };

    public static IResult Duplicate(string field) => Results.Problem(
        statusCode: StatusCodes.Status409Conflict, title: "Conflict",
        extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.duplicate_person, ["field"] = field });
}
