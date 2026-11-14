using Lms.Domain.Grading;
using Lms.Infrastructure.Grading.Scales;
using Lms.Shared;
using Lms.Shared.Grading;

namespace Lms.Api.Features.GradingScales;

/// <summary>Field rules of the scale and weight-template bodies (M5 plan Task 4, FR-GRD-02/03).</summary>
internal static class GradingScaleValidators
{
    public const int MaxCode = 20;
    public const int MaxDescriptor = 500;
    public const int MaxItems = 50;

    public static IResult Invalid(string field, string key) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { [field] = [key] },
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });

    /// <summary>Validates a scale body; the bands come back as specs in the caller's order, or a problem result.</summary>
    public static (IReadOnlyList<BandSpec>? Bands, IResult? Problem) ValidateScale(SaveGradingScaleRequest? request)
    {
        if (request is null)
        {
            return (null, Invalid("Body", "required"));
        }

        if (Name(request.NameAr, "NameAr") is { } arProblem)
        {
            return (null, arProblem);
        }

        if (Name(request.NameEn, "NameEn") is { } enProblem)
        {
            return (null, enProblem);
        }

        if (!ScaleKinds.All.Contains(request.Kind))
        {
            return (null, Invalid("Kind", "unknown"));
        }

        if (request.Bands is null)
        {
            return (null, Invalid("Bands", "required"));
        }

        var specs = new List<BandSpec>();
        foreach (var band in request.Bands)
        {
            if (band is null || Length(band.LabelAr, GradingLimits.NameMaxLength) || Length(band.LabelEn, GradingLimits.NameMaxLength)
                || Length(band.DescriptorAr, MaxDescriptor) || Length(band.DescriptorEn, MaxDescriptor)
                || (band.Code?.Trim().Length ?? 0) > MaxCode)
            {
                return (null, Invalid("Bands", "too_long"));
            }

            if (request.Kind != ScaleKinds.Gpa && band.Points is not null)
            {
                return (null, Invalid("Bands", "points_only_for_gpa"));
            }

            specs.Add(new BandSpec(
                band.MinPercent, band.MaxPercent, band.Code ?? string.Empty, Clean(band.LabelAr), Clean(band.LabelEn),
                Clean(band.DescriptorAr), Clean(band.DescriptorEn), band.Points));
        }

        var check = ScaleBandValidator.Validate(
            specs.Select((b, i) => new ScaleBandInput(i, b.MinPercent, b.MaxPercent, b.Code, b.Points)).ToList(),
            request.Kind == ScaleKinds.Gpa, GradingLimits.MaxBands);
        return check.IsValid
            ? (specs, null)
            : (null, Results.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "Bad request", detail: "The bands must cover 0 to 100 without gaps or overlaps.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = ProblemCodes.scale_bands_invalid,
                    ["problems"] = check.Problems.Select(p => new { kind = p.Kind, from = p.From, to = p.To }).ToList(),
                }));
    }

    public static (IReadOnlyList<ItemSpec>? Items, IResult? Problem) ValidateTemplate(SaveWeightTemplateRequest? request)
    {
        if (request is null)
        {
            return (null, Invalid("Body", "required"));
        }

        if (Name(request.NameAr, "NameAr") is { } arProblem)
        {
            return (null, arProblem);
        }

        if (Name(request.NameEn, "NameEn") is { } enProblem)
        {
            return (null, enProblem);
        }

        if (request.Items is null || request.Items.Count == 0 || request.Items.Count > MaxItems)
        {
            return (null, Invalid("Items", request.Items is { Count: > MaxItems } ? "too_many" : "required"));
        }

        var specs = new List<ItemSpec>();
        foreach (var item in request.Items)
        {
            if (item is null || Name(item.NameAr, "Items") is not null || Name(item.NameEn, "Items") is not null)
            {
                return (null, Invalid("Items", "name_required"));
            }

            if (item.Weight is < GradingLimits.WeightMin or > GradingLimits.WeightMax)
            {
                return (null, Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest, title: "Bad request", detail: "A weight must be between 0 and 100.",
                    extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.weights_invalid }));
            }

            specs.Add(new ItemSpec(item.NameAr.Trim(), item.NameEn.Trim(), item.Weight));
        }

        return (specs, null);
    }

    private static IResult? Name(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? Invalid(field, "required")
        : value.Trim().Length > GradingLimits.NameMaxLength ? Invalid(field, "too_long")
        : null;

    private static bool Length(string? value, int max) => value is not null && value.Trim().Length > max;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
