using Lms.Api.Features.Classes;
using Lms.Domain.Content;
using Lms.Domain.LiveSessions;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;

namespace Lms.Api.Features.LiveSessions;

/// <summary>The shape rules of the live session requests (FR-VC-01); the module rules are the module writer's, the recurrence rules the expander's.</summary>
internal static class LiveSessionValidators
{
    /// <summary>The common body of create and update, as one record so both requests share the checks.</summary>
    public sealed record Body(ModuleSettingsInput? Module, DateTime StartsAtUtc, int DurationMinutes, string Scope, LiveRecurrenceInput? Recurrence);

    public static Body Of(CreateLiveSessionRequest r) => new(r.Module, r.StartsAtUtc, r.DurationMinutes, SeriesEditScopes.This, r.Recurrence);

    public static Body Of(UpdateLiveSessionRequest r) => new(r.Module, r.StartsAtUtc, r.DurationMinutes, r.Scope, r.Recurrence);

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

        if (!CompletionRules.ForActivity(ModuleKinds.LiveSession).Contains(module.CompletionRule))
        {
            return ClassValidators.Invalid(nameof(ModuleSettingsInput.CompletionRule), "invalid_value");
        }

        if (body.DurationMinutes is < LiveSessionLimits.DurationMinMinutes or > LiveSessionLimits.DurationMaxMinutes)
        {
            return ClassValidators.Invalid(nameof(Body.DurationMinutes), "out_of_range");
        }

        return SeriesEditScopes.All.Contains(body.Scope) ? null : ClassValidators.Invalid(nameof(Body.Scope), "invalid_value");
    }

    public static RecurrenceRule? RuleOf(LiveRecurrenceInput? input) =>
        input is null ? null : new RecurrenceRule(input.IntervalWeeks, input.DaysOfWeek ?? [], input.UntilUtc, input.Count, input.TimeZoneId ?? string.Empty);

    public static ModuleMetadata MetadataOf(ModuleSettingsInput m) => new(
        m.TitleAr.Trim(), m.TitleEn.Trim(), m.Visibility, m.VisibleFromUtc, m.VisibleToUtc, m.Target, m.GroupId, m.CompletionRule,
        m.CompletionMinScore);

    private static bool InRange(string? title) =>
        title is not null && title.Trim().Length is >= LiveSessionLimits.TitleMinLength and <= LiveSessionLimits.TitleMaxLength;
}
