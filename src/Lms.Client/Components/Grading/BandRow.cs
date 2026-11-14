using Lms.Shared.Grading;

namespace Lms.Client.Components.Grading;

/// <summary>One editable band of a scale in the dialog.</summary>
public sealed class BandRow
{
    public string Code { get; set; } = "";
    public decimal? MinPercent { get; set; }
    public decimal? MaxPercent { get; set; }
    public string LabelAr { get; set; } = "";
    public string LabelEn { get; set; } = "";
    public string DescriptorAr { get; set; } = "";
    public string DescriptorEn { get; set; } = "";
    public decimal? Points { get; set; }

    public static BandRow From(GradingScaleBandDto b) => new()
    {
        Code = b.Code, MinPercent = b.MinPercent, MaxPercent = b.MaxPercent, LabelAr = b.LabelAr ?? "", LabelEn = b.LabelEn ?? "",
        DescriptorAr = b.DescriptorAr ?? "", DescriptorEn = b.DescriptorEn ?? "", Points = b.Points,
    };

    public SaveGradingScaleBandRequest ToRequest(int order) => new(order, MinPercent ?? 0m, MaxPercent ?? 0m, Code.Trim(),
        Blank(LabelAr), Blank(LabelEn), Blank(DescriptorAr), Blank(DescriptorEn), Points);

    private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
