using Bunit;
using Lms.Client.Pages;
using Lms.Client.Tests.Support;
using System.Globalization;

namespace Lms.Client.Tests.Pages;

public sealed class ForbiddenTests : ClientTestContext
{
    [Theory]
    [InlineData("ar", "ليست لديك صلاحية")]
    [InlineData("en", "You do not have permission")]
    public void Forbidden_page_is_localized(string culture, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var cut = Render<Forbidden>();
        Assert.Contains(expected, cut.Find("[data-testid=forbidden-page]").TextContent);
    }
}
