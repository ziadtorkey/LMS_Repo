using Lms.Client.Services;

namespace Lms.Client.Tests.Services;

public sealed class PluralsTests
{
    [Theory]
    [InlineData(0, "Many")]
    [InlineData(1, "One")]
    [InlineData(2, "Two")]
    [InlineData(3, "Few")]
    [InlineData(10, "Few")]
    [InlineData(11, "Many")]
    [InlineData(100, "Many")]
    public void Arabic_has_four_forms(int n, string expected) => Assert.Equal(expected, Plurals.Suffix(n, arabic: true));

    [Theory]
    [InlineData(0, "Many")]
    [InlineData(1, "One")]
    [InlineData(2, "Many")]
    [InlineData(3, "Many")]
    [InlineData(11, "Many")]
    public void English_has_singular_and_plural(int n, string expected) => Assert.Equal(expected, Plurals.Suffix(n, arabic: false));

    [Fact]
    public async Task A_retry_after_header_is_read_in_seconds()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"code\":\"provider_unavailable\"}", System.Text.Encoding.UTF8, "application/problem+json"),
        };
        response.Headers.TryAddWithoutValidation("Retry-After", "300");

        var problem = await Lms.Client.Services.Api.ApiProblemException.FromResponseAsync(response);

        Assert.Equal(300, problem.RetryAfterSeconds);
    }

    [Fact]
    public async Task A_missing_retry_after_header_is_null()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"code\":\"provider_unavailable\"}", System.Text.Encoding.UTF8, "application/problem+json"),
        };

        var problem = await Lms.Client.Services.Api.ApiProblemException.FromResponseAsync(response);

        Assert.Null(problem.RetryAfterSeconds);
    }
}
