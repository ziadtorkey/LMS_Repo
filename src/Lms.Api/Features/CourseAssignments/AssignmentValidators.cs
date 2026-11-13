using Lms.Api.Features.Classes;
using Lms.Api.Infrastructure;
using Lms.Domain.Content;
using Lms.Shared;
using Lms.Shared.Assignments;
using Lms.Shared.Content;

namespace Lms.Api.Features.CourseAssignments;

/// <summary>The shape rules of the assignment requests (FR-ASG-01); the module rules are the module writer's.</summary>
internal static class AssignmentValidators
{
    public const int MaxAttachments = 10;
    public const int MaxFiles = 10;
    public const decimal MinGrade = 0.01m;
    public const decimal MaxGradeLimit = 1000m;

    /// <summary>The common body of create and update, as one tuple so both requests share the checks.</summary>
    public sealed record Body(
        ModuleSettingsInput? Module, string? InstructionsHtmlAr, string? InstructionsHtmlEn, IReadOnlyList<Guid>? AttachmentAssetIds,
        DateTime? DueAtUtc, decimal MaxGrade, string? LatePolicy, int? PenaltyPercent, string? ResubmissionRule, bool AllowText, bool AllowFiles);

    public static Body Of(CreateCourseAssignmentRequest r) => new(
        r.Module, r.InstructionsHtmlAr, r.InstructionsHtmlEn, r.AttachmentAssetIds, r.DueAtUtc, r.MaxGrade, r.LatePolicy, r.PenaltyPercent,
        r.ResubmissionRule, r.AllowText, r.AllowFiles);

    public static Body Of(UpdateCourseAssignmentRequest r) => new(
        r.Module, r.InstructionsHtmlAr, r.InstructionsHtmlEn, r.AttachmentAssetIds, r.DueAtUtc, r.MaxGrade, r.LatePolicy, r.PenaltyPercent,
        r.ResubmissionRule, r.AllowText, r.AllowFiles);

    public static IResult? Validate(Body? body)
    {
        if (body?.Module is null)
        {
            return ClassValidators.Invalid("body", "required");
        }

        var module = body.Module;
        if (!InRange(module.TitleAr) || !InRange(module.TitleEn))
        {
            return ClassValidators.Invalid(nameof(ModuleSettingsInput.TitleAr), "length_1_200");
        }

        if (!CompletionRules.ForActivity(ModuleKinds.Assignment).Contains(module.CompletionRule))
        {
            return ClassValidators.Invalid(nameof(ModuleSettingsInput.CompletionRule), "invalid_value");
        }

        if (body.MaxGrade is < MinGrade or > MaxGradeLimit)
        {
            return ClassValidators.Invalid(nameof(Body.MaxGrade), "out_of_range");
        }

        if (!body.AllowText && !body.AllowFiles)
        {
            return ClassValidators.Invalid(nameof(Body.AllowText), "one_required");
        }

        if (body.LatePolicy is null || !LatePolicies.All.Contains(body.LatePolicy))
        {
            return ClassValidators.Invalid(nameof(Body.LatePolicy), "invalid_value");
        }

        if (body.ResubmissionRule is null || !ResubmissionRules.All.Contains(body.ResubmissionRule))
        {
            return ClassValidators.Invalid(nameof(Body.ResubmissionRule), "invalid_value");
        }

        var penalty = body.LatePolicy == LatePolicies.Penalty;
        if (penalty != (body.PenaltyPercent is not null)
            || (body.PenaltyPercent is { } p && p is < 1 or > 100)
            || (body.LatePolicy == LatePolicies.Block && body.DueAtUtc is null))
        {
            return ApiProblems.BadRequest(ProblemCodes.late_policy_invalid);
        }

        var attachments = body.AttachmentAssetIds ?? [];
        if (attachments.Count > MaxAttachments || attachments.Distinct().Count() != attachments.Count)
        {
            return ClassValidators.Invalid(nameof(Body.AttachmentAssetIds), "invalid_list");
        }

        return null;
    }

    /// <summary>Sanitises one instructions body; null when empty. The caller catches <see cref="RichTextTooLongException"/>.</summary>
    public static string? Clean(IRichTextSanitizer sanitizer, string? html)
    {
        var clean = sanitizer.Sanitize(html);
        return clean.Length == 0 ? null : clean;
    }

    public static IResult? ValidateSubmit(SubmitAssignmentRequest? r)
    {
        if (r is null)
        {
            return ClassValidators.Invalid("body", "required");
        }

        var files = r.FileAssetIds ?? [];
        return files.Count > MaxFiles || files.Distinct().Count() != files.Count
            ? ClassValidators.Invalid(nameof(SubmitAssignmentRequest.FileAssetIds), "invalid_list")
            : null;
    }

    private static bool InRange(string? value) => value is { } v && v.Trim().Length is >= 1 and <= 200;
}
