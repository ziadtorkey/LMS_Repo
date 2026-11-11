using Lms.Api.Infrastructure;
using Lms.Shared;
using Lms.Shared.AcademicYears;

namespace Lms.Api.Features.AcademicYears;

/// <summary>Request validation. Failures are 400 <c>validation_failed</c>; the detail names the offending field (the client localises by code).</summary>
public static class AcademicYearValidators
{
    private const int MaxNameLength = 200;

    public static IResult? Validate(SaveAcademicYearRequest request) =>
        ValidateNames(request.NameAr, request.NameEn) ?? ValidateDates(request.StartDate, request.EndDate);

    public static IResult? Validate(SaveTermRequest request) =>
        ValidateNames(request.NameAr, request.NameEn) ?? ValidateDates(request.StartDate, request.EndDate);

    private static IResult? ValidateNames(string? nameAr, string? nameEn)
    {
        if (string.IsNullOrWhiteSpace(nameAr) || nameAr.Length > MaxNameLength)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "NameAr is required (max 200 characters).");
        }

        return string.IsNullOrWhiteSpace(nameEn) || nameEn.Length > MaxNameLength
            ? ApiProblems.BadRequest(ProblemCodes.validation_failed, "NameEn is required (max 200 characters).")
            : null;
    }

    private static IResult? ValidateDates(DateOnly start, DateOnly end) =>
        start < end ? null : ApiProblems.BadRequest(ProblemCodes.validation_failed, "StartDate must be before EndDate.");
}
