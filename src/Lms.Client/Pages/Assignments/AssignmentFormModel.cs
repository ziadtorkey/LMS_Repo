using Lms.Client.Components.Content;
using Lms.Shared.Assignments;
using Lms.Shared.Content;

namespace Lms.Client.Pages.Assignments;

/// <summary>Editable state of the assignment dialog. The due date is a local date + time, sent as UTC.</summary>
public sealed class AssignmentFormModel
{
    public Guid? LessonKey { get; set; }
    public ModuleFormModel Module { get; } = new() { CompletionRule = CompletionRules.Submit };
    public string? InstructionsAr { get; set; }
    public string? InstructionsEn { get; set; }
    public List<AssetDto> Attachments { get; } = [];
    public DateTime? DueDate { get; set; }
    public TimeSpan? DueTime { get; set; }
    public decimal MaxGrade { get; set; } = 100m;
    public string LatePolicy { get; set; } = LatePolicies.Allow;
    public int? PenaltyPercent { get; set; }
    public string ResubmissionRule { get; set; } = ResubmissionRules.None;
    public bool AllowText { get; set; } = true;
    public bool AllowFiles { get; set; } = true;
    /// <summary>Minimum score (0-100) for the Score completion rule.</summary>
    public decimal? CompletionMinScore { get; set; }

    public DateTime? DueUtc => DueDate is null
        ? null
        : DateTime.SpecifyKind(DueDate.Value.Date + (DueTime ?? TimeSpan.Zero), DateTimeKind.Local).ToUniversalTime();

    public static AssignmentFormModel From(CourseAssignmentDto a)
    {
        var m = new AssignmentFormModel
        {
            LessonKey = a.LessonKey, InstructionsAr = a.InstructionsHtmlAr, InstructionsEn = a.InstructionsHtmlEn,
            MaxGrade = a.MaxGrade, LatePolicy = a.LatePolicy, PenaltyPercent = a.PenaltyPercent,
            ResubmissionRule = a.ResubmissionRule, AllowText = a.AllowText, AllowFiles = a.AllowFiles,
        };
        m.Attachments.AddRange(a.Attachments);
        var detail = new ModuleFormModel
        {
            TitleAr = a.Module.TitleAr, TitleEn = a.Module.TitleEn, Visibility = a.Module.Visibility, Target = a.Module.Target,
            GroupId = a.Module.GroupId, CompletionRule = a.Module.CompletionRule,
        };
        if (a.Module.VisibleFromUtc is { } f) { var l = DateTime.SpecifyKind(f, DateTimeKind.Utc).ToLocalTime(); detail.FromDate = l.Date; detail.FromTime = l.TimeOfDay; }
        if (a.Module.VisibleToUtc is { } t) { var l = DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime(); detail.ToDate = l.Date; detail.ToTime = l.TimeOfDay; }
        CopyModule(detail, m.Module);
        if (a.DueAtUtc is { } due) { var l = DateTime.SpecifyKind(due, DateTimeKind.Utc).ToLocalTime(); m.DueDate = l.Date; m.DueTime = l.TimeOfDay; }
        return m;
    }

    private static void CopyModule(ModuleFormModel from, ModuleFormModel to)
    {
        to.TitleAr = from.TitleAr; to.TitleEn = from.TitleEn; to.Visibility = from.Visibility; to.Target = from.Target;
        to.GroupId = from.GroupId; to.CompletionRule = from.CompletionRule;
        to.FromDate = from.FromDate; to.FromTime = from.FromTime; to.ToDate = from.ToDate; to.ToTime = from.ToTime;
    }

    public ModuleSettingsInput ToModuleSettings()
    {
        var scheduled = Module.Visibility == ModuleVisibilities.Scheduled;
        return new ModuleSettingsInput(Module.TitleAr.Trim(), Module.TitleEn.Trim(), Module.Visibility,
            scheduled ? Module.FromUtc : null, scheduled ? Module.ToUtcValue : null, Module.Target, Module.GroupId, Module.CompletionRule, Module.CompletionRule == CompletionRules.Score ? CompletionMinScore : null);
    }

    public IReadOnlyList<Guid> AttachmentIds => Attachments.Select(x => x.Id).ToList();
}
