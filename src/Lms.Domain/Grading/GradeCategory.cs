using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Grading;

/// <summary>
/// A category of a gradebook with its weight (0 to 100). The built-in <c>Uncategorised</c> category (weight 0) holds new auto items until the
/// teacher moves them (L3). A gradebook with weights that do not total 100 is "invalid" but still computes and still accepts grades.
/// </summary>
[Audited]
public sealed class GradeCategory : UnitOwnedEntity, ILiveKeyed
{
    private GradeCategory()
    {
        NameAr = NameEn = string.Empty;
    }

    public GradeCategory(Gradebook gradebook, string nameAr, string nameEn, decimal weight, int order, bool isUncategorised = false)
    {
        PlaceLike(gradebook);
        GradebookId = gradebook.Id;
        NameAr = nameAr;
        NameEn = nameEn;
        Weight = weight;
        Order = order;
        IsUncategorised = isUncategorised;
        UncategorisedKey = isUncategorised ? gradebook.Id : null;
    }

    public Guid GradebookId { get; private set; }
    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public decimal Weight { get; private set; }
    public int Order { get; private set; }
    public bool IsUncategorised { get; private set; }

    /// <summary>The gradebook id while this is the live Uncategorised category: unique, so a gradebook has exactly one.</summary>
    public Guid? UncategorisedKey { get; private set; }

    public void Update(string nameAr, string nameEn, decimal weight, int order)
    {
        NameAr = nameAr;
        NameEn = nameEn;
        Weight = weight;
        Order = order;
    }

    public void ClearLiveKeys() => UncategorisedKey = null;
}
