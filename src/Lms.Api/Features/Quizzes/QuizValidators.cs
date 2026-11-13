using Lms.Api.Features.Classes;
using Lms.Api.Infrastructure;
using Lms.Domain.Content;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.Quizzes;

namespace Lms.Api.Features.Quizzes;

/// <summary>Field rules of the quiz bodies (FR-QZ-01, FR-QZ-02). Facts that need the database are checked by the handler.</summary>
internal static class QuizValidators
{
    public const int MaxDescription = 2_000;
    public const int MaxQuestionText = 4_000;
    public const int MaxOptionText = 1_000;
    public const decimal MinPoints = 0.01m;
    public const decimal MaxPoints = 1_000m;

    public static IResult? Validate(CreateQuizRequest? request) =>
        request is null
            ? ClassValidators.Invalid("body", "required")
            : Validate(request.Module, request.DescriptionAr, request.DescriptionEn, request.Settings);

    public static IResult? Validate(UpdateQuizRequest? request) =>
        request is null
            ? ClassValidators.Invalid("body", "required")
            : Validate(request.Module, request.DescriptionAr, request.DescriptionEn, request.Settings);

    private static IResult? Validate(ModuleSettingsInput? module, string? descriptionAr, string? descriptionEn, QuizSettingsDto? settings)
    {
        if (module is null || settings is null)
        {
            return ClassValidators.Invalid("body", "required");
        }

        if (!ClassValidators.ValidName(module.TitleAr))
        {
            return ClassValidators.Invalid("Module.TitleAr", "name_invalid");
        }

        if (!ClassValidators.ValidName(module.TitleEn))
        {
            return ClassValidators.Invalid("Module.TitleEn", "name_invalid");
        }

        if (module.Visibility is null || !ModuleVisibilities.All.Contains(module.Visibility))
        {
            return ClassValidators.Invalid("Module.Visibility", "visibility_invalid");
        }

        if (module.Target is null || !ModuleTargets.All.Contains(module.Target))
        {
            return ClassValidators.Invalid("Module.Target", "target_invalid");
        }

        if (module.CompletionRule is null || !CompletionRules.ForActivity(ModuleKinds.Quiz).Contains(module.CompletionRule))
        {
            return ClassValidators.Invalid("Module.CompletionRule", "rule_invalid");
        }

        if (module.CompletionMinScore is < 0 or > 100 || (module.CompletionMinScore is not null && module.CompletionRule != CompletionRules.Score))
        {
            return ClassValidators.Invalid("Module.CompletionMinScore", "min_score_invalid");
        }

        if ((descriptionAr?.Length ?? 0) > MaxDescription || (descriptionEn?.Length ?? 0) > MaxDescription)
        {
            return ClassValidators.Invalid("Description", "too_long");
        }

        return ValidateSettings(settings);
    }

    /// <summary>The settings rules that need no question count; the pool is checked against the count by <see cref="PoolProblem"/>.</summary>
    public static IResult? ValidateSettings(QuizSettingsDto settings)
    {
        if (settings.TimeLimitMinutes is < 1 or > 600)
        {
            return ClassValidators.Invalid("Settings.TimeLimitMinutes", "time_limit_invalid");
        }

        if (settings.MaxAttempts is < 1 or > 20)
        {
            return ClassValidators.Invalid("Settings.MaxAttempts", "max_attempts_invalid");
        }

        if (!GradingMethods.All.Contains(settings.GradingMethod ?? string.Empty))
        {
            return ClassValidators.Invalid("Settings.GradingMethod", "grading_method_invalid");
        }

        if (!ReviewPolicies.All.Contains(settings.ReviewPolicy ?? string.Empty))
        {
            return ClassValidators.Invalid("Settings.ReviewPolicy", "review_policy_invalid");
        }

        return settings is { OpensAtUtc: { } opens, ClosesAtUtc: { } closes } && opens >= closes
            ? ClassValidators.Invalid("Settings.ClosesAtUtc", "range_invalid")
            : null;
    }

    /// <summary>A draw count must be between 1 and the number of questions (else 400 <c>pool_draw_invalid</c>).</summary>
    public static IResult? PoolProblem(int? drawCount, int questionCount) =>
        drawCount is { } draw && (draw < 1 || draw > questionCount)
            ? ApiProblems.BadRequest(ProblemCodes.pool_draw_invalid)
            : null;

    public static IResult? Validate(QuizQuestionInput? input)
    {
        if (input is null)
        {
            return ClassValidators.Invalid("body", "required");
        }

        if (!QuestionTypes.All.Contains(input.Type ?? string.Empty))
        {
            return ClassValidators.Invalid("Type", "type_invalid");
        }

        var text = input.Text?.Trim() ?? string.Empty;
        if (text.Length is < 1 or > MaxQuestionText)
        {
            return ClassValidators.Invalid("Text", "text_invalid");
        }

        if (input.Points is < MinPoints or > MaxPoints || decimal.Round(input.Points, 2) != input.Points)
        {
            return ClassValidators.Invalid("Points", "points_invalid");
        }

        var options = input.Options ?? [];
        if (options.Any(o => o is null || (o.Text?.Trim().Length ?? 0) is < 1 or > MaxOptionText))
        {
            return ClassValidators.Invalid("Options", "option_text_invalid");
        }

        var correct = options.Count(o => o.IsCorrect);
        var optionsOk = input.Type switch
        {
            QuestionTypes.SingleChoice => options.Count is >= 2 and <= 10 && correct == 1,
            QuestionTypes.MultipleChoice => options.Count is >= 2 and <= 10 && correct >= 1,
            QuestionTypes.TrueFalse => options.Count == 2 && correct == 1,
            _ => options.Count == 0, // Essay
        };
        return optionsOk ? null : ClassValidators.Invalid("Options", "options_invalid");
    }

    public static ModuleMetadata Metadata(ModuleSettingsInput m) =>
        new(m.TitleAr.Trim(), m.TitleEn.Trim(), m.Visibility, m.VisibleFromUtc, m.VisibleToUtc, m.Target, m.GroupId, m.CompletionRule, m.CompletionMinScore);
}
