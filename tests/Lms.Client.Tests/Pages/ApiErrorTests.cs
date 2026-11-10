using Microsoft.Extensions.DependencyInjection;
using Bunit;
using Lms.Client.Pages;
using Lms.Client.Services;
using Lms.Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace Lms.Client.Tests.Pages;

public sealed class ApiErrorTests : ClientTestContext
{
    [Fact]
    public void Api_403_shows_forbidden_page_without_error_ui()
    {
        Api.Failure = new ApiForbiddenException();

        var cut = Render<Home>();

        Assert.EndsWith("/forbidden", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Empty(cut.FindAll("[data-testid=api-error]"));
    }

    [Theory]
    [InlineData("ar", "تعذر تحميل البيانات")]
    [InlineData("en", "Could not load data")]
    public void Other_api_errors_show_a_localized_error_state(string culture, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        Api.Failure = new HttpRequestException("boom");

        var cut = Render<Home>();

        Assert.Contains(expected, cut.Find("[data-testid=api-error]").TextContent);
    }
}
