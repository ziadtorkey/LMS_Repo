using Lms.Infrastructure.AcademicYears;

namespace Lms.UnitTests.AcademicYears;

public sealed class TermTemplateTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Template_names_have_ar_and_en(int count)
    {
        var names = TermTemplate.NamesFor(count);

        Assert.Equal(count, names.Count);
        Assert.Equal(Enumerable.Range(1, count), names.Select(n => n.Order));
        Assert.All(names, n =>
        {
            Assert.Contains(n.NameAr, c => c is >= '\u0600' and <= '\u06FF'); // Arabic script
            Assert.DoesNotContain(n.NameEn, c => c >= 128);
        });
        Assert.Equal(names.Count, names.Select(n => n.NameEn).Distinct().Count());
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void Only_two_or_three_terms_are_valid(int count, bool valid) => Assert.Equal(valid, TermTemplate.IsValid(count));
}
