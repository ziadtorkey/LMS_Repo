namespace Lms.Shared.AcademicYears;

/// <summary>The name of the term at <c>Order</c> (1-based) in a structure (an Arabic and an English name, CLAUDE.md rule 4).</summary>
public sealed record TermNameDto(int Order, string NameAr, string NameEn);

/// <summary>
/// The term structure of a year (M5 L8, D62): two or three terms (<c>AcademicYears.TermsPerYear</c>, default 2) with a name template per term
/// order. PUT <c>/api/academic-years/{id}/term-structure</c> (<c>AcademicYears.Update</c>) is allowed only while the year is <c>Planned</c> and no
/// term has a gradebook, slot or attendance session (409 <c>terms_have_dependants</c>); existing terms are never deleted or renamed by the switch,
/// the admin edits them. GET answers the structure the unit would use (the setting value and the names).
/// </summary>
public sealed record TermStructureDto(int TermsPerYear, IReadOnlyList<TermNameDto> TermNames);
