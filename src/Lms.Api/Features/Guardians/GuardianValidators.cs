using Lms.Api.Infrastructure;
using Lms.Domain.People;
using Lms.Infrastructure.People.Guardians;
using Lms.Shared;
using Lms.Shared.People;

namespace Lms.Api.Features.Guardians;

/// <summary>Maps the linker's refusals to problem responses and the contract inputs to the domain ones.</summary>
internal static class GuardianValidators
{
    public static GuardianData ToDomain(GuardianDataInput input) =>
        new(input.NameAr, input.NameEn, input.Mobile, input.Email, input.NationalId);

    public static GuardianLink ToDomain(GuardianLinkInput input) =>
        new(input.ExistingGuardianId, input.NewGuardian is null ? null : ToDomain(input.NewGuardian), input.Relation, input.IsPrimary);

    public static IResult ToProblem(GuardianLinkException ex) => ex.Code switch
    {
        GuardianLinker.NotFoundCode => ApiProblems.NotFound(),
        ProblemCodes.validation_failed => Results.ValidationProblem(
            new Dictionary<string, string[]> { [ex.Field ?? "body"] = ["invalid"] },
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed }),
        ProblemCodes.duplicate_person => Results.Problem(
            statusCode: StatusCodes.Status409Conflict, title: "Conflict",
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.duplicate_person, ["field"] = ex.Field }),
        _ => ApiProblems.Conflict(ex.Code),
    };
}
