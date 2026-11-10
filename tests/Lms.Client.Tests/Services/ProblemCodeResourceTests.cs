using System.Globalization;
using Bunit;
using Lms.Client.Resources;
using Lms.Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Tests.Services;

public sealed class ProblemCodeResourceTests : ClientTestContext
{
    private static readonly string[] Codes = typeof(Lms.Shared.ProblemCodes)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .Concat(["MissingPermission", "OutOfScope", "invalid_context", "context_required"]).ToArray();

    [Fact]
    public void ProblemCodes_are_discovered() => Assert.NotEmpty(Codes);

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Every_problem_code_has_an_error_resource(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        var missing = Codes.Where(c => l[$"Error_{c}"].ResourceNotFound).ToList();

        Assert.Empty(missing);
    }
}
