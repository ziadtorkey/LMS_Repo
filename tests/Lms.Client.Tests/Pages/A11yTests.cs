using Bunit;
using Lms.Client.Layout;
using Lms.Client.Pages;
using Lms.Client.Services;
using Lms.Client.Tests.Support;
using System.Globalization;

namespace Lms.Client.Tests.Pages;

public sealed class A11yTests : ClientTestContext
{
    [Theory]
    [InlineData("ar", "en")]
    [InlineData("en", "ar")]
    public void Language_button_is_marked_with_the_language_it_switches_to(string current, string target)
    {
        Culture.Current = new CultureInfo(current);
        var cut = Render<LanguageSwitcher>();
        Assert.Equal(target, cut.Find("[data-testid=lang-switch]").GetAttribute("lang"));
    }

    [Fact]
    public void NotFound_sets_a_page_title() =>
        Assert.NotEmpty(Render<NotFound>().FindComponents<Microsoft.AspNetCore.Components.Web.PageTitle>());

    [Fact]
    public void Arabic_culture_uses_the_gregorian_calendar()
    {
        var culture = CultureSetup.WithGregorianCalendar(new CultureInfo("ar-SA"));
        Assert.IsType<GregorianCalendar>(culture.DateTimeFormat.Calendar);
        Assert.Equal("ar-SA", culture.Name);
    }
}
