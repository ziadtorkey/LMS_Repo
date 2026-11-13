using Lms.Api.Features.Classes;
using Lms.Api.Infrastructure;
using Lms.Shared;
using Lms.Shared.Content;

namespace Lms.Api.Features.Content;

/// <summary>Field rules of the module bodies (CreateModuleRequest, UpdateModuleRequest). Facts that need the database are checked by the handler.</summary>
internal static class ModuleValidators
{
    public const int MaxTitle = 200;
    public const int MaxBody = 200_000;
    public const int MaxUrl = 2_000;

    public static IResult? Validate(CreateModuleRequest? request)
    {
        if (request is null)
        {
            return ClassValidators.Invalid("body", "required");
        }

        if (!ModuleKinds.Content.Contains(request.Kind))
        {
            return ClassValidators.Invalid(nameof(request.Kind), "kind_invalid");
        }

        return ValidateFields(
            request.Kind, request.TitleAr, request.TitleEn, request.Visibility, request.VisibleFromUtc, request.VisibleToUtc,
            request.Target, request.GroupId, request.CompletionRule, request.CompletionMinScore, request.Content);
    }

    public static IResult? Validate(UpdateModuleRequest? request, string kind)
    {
        if (request is null)
        {
            return ClassValidators.Invalid("body", "required");
        }

        return ValidateFields(
            kind, request.TitleAr, request.TitleEn, request.Visibility, request.VisibleFromUtc, request.VisibleToUtc,
            request.Target, request.GroupId, request.CompletionRule, request.CompletionMinScore, request.Content);
    }

    private static IResult? ValidateFields(
        string kind, string? titleAr, string? titleEn, string? visibility, DateTime? from, DateTime? to, string? target, Guid? groupId,
        string? rule, decimal? minScore, ModuleContentInput? content)
    {
        if (!ClassValidators.ValidName(titleAr))
        {
            return ClassValidators.Invalid("TitleAr", "name_invalid");
        }

        if (!ClassValidators.ValidName(titleEn))
        {
            return ClassValidators.Invalid("TitleEn", "name_invalid");
        }

        if (visibility is null || !ModuleVisibilities.All.Contains(visibility))
        {
            return ClassValidators.Invalid("Visibility", "visibility_invalid");
        }

        var scheduled = visibility == ModuleVisibilities.Scheduled;
        if (scheduled ? (from is null && to is null) || (from is { } f && to is { } t && f >= t) : from is not null || to is not null)
        {
            return ApiProblems.BadRequest(ProblemCodes.module_visibility_range_invalid);
        }

        if (target is null || !ModuleTargets.All.Contains(target))
        {
            return ClassValidators.Invalid("Target", "target_invalid");
        }

        if ((target == ModuleTargets.Group) != (groupId is not null))
        {
            return ApiProblems.BadRequest(ProblemCodes.module_target_invalid);
        }

        if (rule is null || !CompletionRules.ForContent.Contains(rule))
        {
            return ClassValidators.Invalid("CompletionRule", "rule_invalid");
        }

        // The minimum score belongs to the Score rule, which no content kind uses.
        if (minScore is not null)
        {
            return ClassValidators.Invalid("CompletionMinScore", "min_score_invalid");
        }

        return ValidateContent(kind, content);
    }

    private static IResult? ValidateContent(string kind, ModuleContentInput? content)
    {
        content ??= new ModuleContentInput();
        switch (kind)
        {
            case ModuleKinds.Page:
                if (content.AssetId is not null || content.Url is not null)
                {
                    return ClassValidators.Invalid("Content", "content_invalid");
                }

                var ar = content.BodyHtmlAr;
                var en = content.BodyHtmlEn;
                if (string.IsNullOrWhiteSpace(ar) && string.IsNullOrWhiteSpace(en))
                {
                    return ClassValidators.Invalid("Content", "body_required");
                }

                return (ar?.Length ?? 0) > MaxBody || (en?.Length ?? 0) > MaxBody
                    ? ClassValidators.Invalid("Content", "body_too_long")
                    : null;
            case ModuleKinds.Link:
                if (content.AssetId is not null || content.BodyHtmlAr is not null || content.BodyHtmlEn is not null)
                {
                    return ClassValidators.Invalid("Content", "content_invalid");
                }

                return IsHttpUrl(content.Url) ? null : ClassValidators.Invalid("Content", "url_invalid");
            case ModuleKinds.File:
            case ModuleKinds.Video:
            case ModuleKinds.Audio:
                // The asset itself (same class course, accepted kind) is checked by the handler, which can read it.
                if (content.BodyHtmlAr is not null || content.BodyHtmlEn is not null || content.Url is not null)
                {
                    return ClassValidators.Invalid("Content", "content_invalid");
                }

                return content.AssetId is null ? ClassValidators.Invalid("Content", "asset_required") : null;
            default:
                return ClassValidators.Invalid("Kind", "kind_invalid");
        }
    }

    public static bool IsHttpUrl(string? url) =>
        url is { Length: > 0 and <= MaxUrl } && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
