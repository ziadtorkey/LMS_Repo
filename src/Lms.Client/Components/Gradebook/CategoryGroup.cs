using Lms.Shared.Grading;

namespace Lms.Client.Components.Gradebook;

/// <summary>A category of the gradebook with its items in order; built once per loaded gradebook so rows can skip re-rendering.</summary>
public sealed record CategoryGroup(GradeCategoryDto Category, IReadOnlyList<GradeItemDto> Items)
{
    public static IReadOnlyList<CategoryGroup> From(IReadOnlyList<GradeCategoryDto> categories, IReadOnlyList<GradeItemDto> items) =>
        [.. categories.OrderBy(c => c.Order)
            .Select(c => new CategoryGroup(c, [.. items.Where(i => i.CategoryId == c.Id).OrderBy(i => i.Order)]))
            .Where(g => g.Items.Count > 0)];
}
