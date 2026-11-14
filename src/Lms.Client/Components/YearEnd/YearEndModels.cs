namespace Lms.Client.Components.YearEnd;

/// <summary>A class the promotion may place a student in (the target year's classes of the mapped next level).</summary>
public sealed record PromotionTargetClass(Guid Id, string NameAr, string NameEn);

/// <summary>The state of a year-end checklist step (spec #814 §2): said in words next to the disc, never only by its colour.</summary>
public enum YearEndStepState
{
    ToDo,
    Attention,
    NotYet,
    Done,
}

/// <summary>One step of the year-end checklist. <see cref="Href"/> null = no link (a step that can't start yet).</summary>
public sealed record YearEndStep(string Title, string Detail, YearEndStepState State, string? ActionText = null, string? Href = null);
