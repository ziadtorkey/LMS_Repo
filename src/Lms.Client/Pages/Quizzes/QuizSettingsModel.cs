using System.Text.Json;
using Lms.Client.Components.Content;
using Lms.Shared.Content;
using Lms.Shared.Quizzes;

namespace Lms.Client.Pages.Quizzes;

/// <summary>Editable quiz settings plus the module settings (title, visibility, target, completion). Dates are local date + time, sent as UTC.</summary>
public sealed class QuizSettingsModel
{
    public ModuleFormModel Module { get; } = new() { CompletionRule = CompletionRules.Score };
    public decimal? CompletionMinScore { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
    public int? TimeLimitMinutes { get; set; }
    public int? MaxAttempts { get; set; } = 1;
    public string GradingMethod { get; set; } = GradingMethods.Highest;
    public bool ShuffleQuestions { get; set; }
    public bool ShuffleOptions { get; set; }
    public DateTime? OpenDate { get; set; }
    public TimeSpan? OpenTime { get; set; }
    public DateTime? CloseDate { get; set; }
    public TimeSpan? CloseTime { get; set; }
    public string ReviewPolicy { get; set; } = ReviewPolicies.Answers;
    public int? DrawCount { get; set; }

    public DateTime? OpensUtc => ToUtc(OpenDate, OpenTime);
    public DateTime? ClosesUtc => ToUtc(CloseDate, CloseTime);

    private static DateTime? ToUtc(DateTime? date, TimeSpan? time) =>
        date is null ? null : DateTime.SpecifyKind(date.Value.Date + (time ?? TimeSpan.Zero), DateTimeKind.Local).ToUniversalTime();

    private static (DateTime? Date, TimeSpan? Time) Split(DateTime? utc)
    {
        if (utc is not { } v) return (null, null);
        var local = DateTime.SpecifyKind(v, DateTimeKind.Utc).ToLocalTime();
        return (local.Date, local.TimeOfDay);
    }

    public static QuizSettingsModel From(QuizDto dto)
    {
        var s = dto.Settings;
        var m = new QuizSettingsModel
        {
            DescriptionAr = dto.DescriptionAr, DescriptionEn = dto.DescriptionEn, TimeLimitMinutes = s.TimeLimitMinutes, MaxAttempts = s.MaxAttempts,
            GradingMethod = s.GradingMethod, ShuffleQuestions = s.ShuffleQuestions, ShuffleOptions = s.ShuffleOptions, ReviewPolicy = s.ReviewPolicy,
            DrawCount = s.DrawCount,
        };
        (m.OpenDate, m.OpenTime) = Split(s.OpensAtUtc);
        (m.CloseDate, m.CloseTime) = Split(s.ClosesAtUtc);
        var n = dto.Module;
        m.Module.TitleAr = n.TitleAr; m.Module.TitleEn = n.TitleEn; m.Module.Visibility = n.Visibility; m.Module.Target = n.Target;
        m.Module.GroupId = n.GroupId; m.Module.CompletionRule = n.CompletionRule;
        (m.Module.FromDate, m.Module.FromTime) = Split(n.VisibleFromUtc);
        (m.Module.ToDate, m.Module.ToTime) = Split(n.VisibleToUtc);
        return m;
    }

    public QuizSettingsDto ToSettings() => new(TimeLimitMinutes, MaxAttempts, GradingMethod, ShuffleQuestions, ShuffleOptions, OpensUtc, ClosesUtc, ReviewPolicy, DrawCount);

    public ModuleSettingsInput ToModuleSettings()
    {
        var scheduled = Module.Visibility == ModuleVisibilities.Scheduled;
        return new ModuleSettingsInput(Module.TitleAr.Trim(), Module.TitleEn.Trim(), Module.Visibility, scheduled ? Module.FromUtc : null,
            scheduled ? Module.ToUtcValue : null, Module.Target, Module.GroupId, Module.CompletionRule,
            Module.CompletionRule == CompletionRules.Score ? CompletionMinScore : null);
    }

    /// <summary>Field errors by field name (resource keys), with the pool checked against the number of questions.</summary>
    public Dictionary<string, string> Validate(int questionCount)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(Module.TitleAr) || string.IsNullOrWhiteSpace(Module.TitleEn)) errors["title"] = "Common_Required";
        if (TimeLimitMinutes is < 1 or > 600) errors["time"] = "Quiz_Err_TimeLimit";
        if (MaxAttempts is < 1 or > 20) errors["attempts"] = "Quiz_Err_Attempts";
        if (OpensUtc is { } o && ClosesUtc is { } c && o >= c) errors["close"] = "Quiz_Err_CloseAfterOpen";
        if (DrawCount is { } d && (d < 1 || d > questionCount)) errors["draw"] = "Quiz_Err_Draw";
        if (Module.Visibility == ModuleVisibilities.Scheduled)
        {
            var (from, to) = (Module.FromUtc, Module.ToUtcValue);
            if ((from is null && to is null) || (from is { } f && to is { } t && f >= t)) errors["visibility"] = "Error_module_visibility_range_invalid";
        }
        if (Module.Target == ModuleTargets.Group && Module.GroupId is null) errors["target"] = "Error_module_target_invalid";
        return errors;
    }

    public string Signature() => JsonSerializer.Serialize(new
    {
        DescriptionAr, DescriptionEn, TimeLimitMinutes, MaxAttempts, GradingMethod, ShuffleQuestions, ShuffleOptions, OpenDate, OpenTime, CloseDate, CloseTime,
        ReviewPolicy, DrawCount, CompletionMinScore,
        Module = new { Module.TitleAr, Module.TitleEn, Module.Visibility, Module.FromDate, Module.FromTime, Module.ToDate, Module.ToTime, Module.Target, Module.GroupId, Module.CompletionRule },
    });
}
