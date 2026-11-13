using Lms.Shared.Content;

namespace Lms.Client.Components.Content;

/// <summary>A group a module can be targeted at (the groups of one ClassCourse; the host supplies them).</summary>
public sealed record ModuleGroupOption(Guid Id, string NameAr, string NameEn);

/// <summary>A lesson a module can be moved to.</summary>
public sealed record ModuleLessonOption(Guid Key, string Title);

/// <summary>Editable state of the module dialog. Visibility dates are kept as local date + time and sent as UTC.</summary>
public sealed class ModuleFormModel
{
    public string TitleAr { get; set; } = "";
    public string TitleEn { get; set; } = "";
    public string Visibility { get; set; } = ModuleVisibilities.Published;
    public DateTime? FromDate { get; set; }
    public TimeSpan? FromTime { get; set; }
    public DateTime? ToDate { get; set; }
    public TimeSpan? ToTime { get; set; }
    public string Target { get; set; } = ModuleTargets.ClassCourse;
    public Guid? GroupId { get; set; }
    public string CompletionRule { get; set; } = CompletionRules.None;
    public Guid? AssetId { get; set; }
    public string? AssetName { get; set; }
    public string? BodyHtmlAr { get; set; }
    public string? BodyHtmlEn { get; set; }
    public string? Url { get; set; }

    public DateTime? FromUtc => ToUtc(FromDate, FromTime);
    public DateTime? ToUtcValue => ToUtc(ToDate, ToTime);

    private static DateTime? ToUtc(DateTime? date, TimeSpan? time) =>
        date is null ? null : DateTime.SpecifyKind(date.Value.Date + (time ?? TimeSpan.Zero), DateTimeKind.Local).ToUniversalTime();

    public static ModuleFormModel From(ModuleDetailDto detail)
    {
        var m = new ModuleFormModel
        {
            TitleAr = detail.Node.TitleAr, TitleEn = detail.Node.TitleEn, Visibility = detail.Node.Visibility,
            Target = detail.Node.Target, GroupId = detail.Node.GroupId, CompletionRule = detail.Node.CompletionRule,
            AssetId = detail.Content.Asset?.Id, AssetName = detail.Content.Asset?.FileName,
            BodyHtmlAr = detail.Content.BodyHtmlAr, BodyHtmlEn = detail.Content.BodyHtmlEn, Url = detail.Content.Url,
        };
        if (detail.Node.VisibleFromUtc is { } f) { var l = DateTime.SpecifyKind(f, DateTimeKind.Utc).ToLocalTime(); m.FromDate = l.Date; m.FromTime = l.TimeOfDay; }
        if (detail.Node.VisibleToUtc is { } t) { var l = DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime(); m.ToDate = l.Date; m.ToTime = l.TimeOfDay; }
        return m;
    }

    public ModuleContentInput ToContent() => new(AssetId, BodyHtmlAr, BodyHtmlEn, Url);
}
