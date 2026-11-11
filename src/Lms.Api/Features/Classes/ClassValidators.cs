using System.Text.RegularExpressions;
using Lms.Shared;
using Lms.Shared.Classes;

namespace Lms.Api.Features.Classes;

/// <summary>Field rules of the class bodies (SaveClassRequest, BulkCreateClassesRequest, CopyClassesRequest).</summary>
internal static partial class ClassValidators
{
    public const int MaxName = 200;
    public const int MaxBulk = 50;
    public const int MaxCopy = 500;
    public const string Token = "{N}";

    [GeneratedRegex("^[A-Za-z0-9_-]{1,50}$")]
    private static partial Regex CodeRegex();

    public static bool IsValidCode(string? code) => code is not null && CodeRegex().IsMatch(code);

    public static IResult Invalid(string field, string key) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { [field] = [key] },
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });

    public static IResult TooMany(string field, int limit) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest, title: "Bad request", detail: $"At most {limit} items are allowed in one request.",
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.too_many_items, ["field"] = field, ["limit"] = limit });

    public static IResult? Validate(SaveClassRequest? request)
    {
        if (request is null)
        {
            return Invalid("body", "required");
        }

        return ValidateFields(request.Code?.Trim(), request.NameAr, request.NameEn, request.Capacity);
    }

    public static IResult? ValidateFields(string? code, string? nameAr, string? nameEn, int? capacity)
    {
        if (!IsValidCode(code))
        {
            return Invalid("Code", "code_invalid");
        }

        if (!ValidName(nameAr))
        {
            return Invalid("NameAr", "name_invalid");
        }

        if (!ValidName(nameEn))
        {
            return Invalid("NameEn", "name_invalid");
        }

        return capacity is < 1 or > 500 ? Invalid("Capacity", "capacity_invalid") : null;
    }

    public static bool ValidName(string? name) => name is not null && name.Trim().Length is >= 1 and <= MaxName;

    public static IResult? Validate(BulkCreateClassesRequest? request)
    {
        if (request is null)
        {
            return Invalid("body", "required");
        }

        if (request.From < 0 || request.From > request.To)
        {
            return Invalid("To", "range_invalid");
        }

        // long: a full-range request must not wrap around in int.
        if ((long)request.To - request.From + 1 > MaxBulk)
        {
            return TooMany("To", MaxBulk);
        }

        foreach (var (field, pattern) in new[]
                 {
                     ("NamePatternAr", request.NamePatternAr), ("NamePatternEn", request.NamePatternEn), ("CodePattern", request.CodePattern),
                 })
        {
            if (pattern is null || pattern.Split(Token).Length != 2)
            {
                return Invalid(field, "pattern_needs_one_token");
            }
        }

        return null;
    }

    public static string Render(string pattern, int n) => pattern.Replace(Token, n.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
