using Lms.Shared.AcademicYears;

namespace Lms.Infrastructure.AcademicYears;

/// <summary>The default names of the terms of a year (D62): two or three terms, an Arabic and an English name per order.</summary>
public static class TermTemplate
{
    public const int MinTerms = 2;
    public const int MaxTerms = 3;

    private static readonly (string Ar, string En)[] Names =
    [
        ("الفصل الدراسي الأول", "First term"),
        ("الفصل الدراسي الثاني", "Second term"),
        ("الفصل الدراسي الثالث", "Third term"),
    ];

    public static bool IsValid(int termsPerYear) => termsPerYear is >= MinTerms and <= MaxTerms;

    /// <summary>The names of the terms of a structure with <paramref name="termsPerYear"/> terms, in order.</summary>
    public static IReadOnlyList<TermNameDto> NamesFor(int termsPerYear) =>
        [.. Names.Take(Math.Clamp(termsPerYear, MinTerms, MaxTerms)).Select((n, i) => new TermNameDto(i + 1, n.Ar, n.En))];
}
