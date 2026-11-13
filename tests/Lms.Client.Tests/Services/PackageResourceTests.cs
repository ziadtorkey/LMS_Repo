using System.Globalization;
using Lms.Client.Resources;
using Lms.Client.Tests.Support;
using Lms.Shared.Packages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Tests.Services;

/// <summary>Every package reject reason has a plain-words label in both languages (the upload dialog shows it per code).</summary>
public sealed class PackageResourceTests : ClientTestContext
{
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Every_reject_reason_has_a_label(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        var missing = PackageRejectReasons.All.Where(r => l[$"Error_package_reason_{r}"].ResourceNotFound).ToList();

        Assert.Empty(missing);
    }
}
