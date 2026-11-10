using Lms.IntegrationTests.Infrastructure;

namespace Lms.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CorrelationIdTests(LmsApiFactory factory)
{
    private async Task<string> RoundTripAsync(string? sent)
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        if (sent is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", sent);
        }

        var response = await client.SendAsync(request);
        return response.Headers.GetValues("X-Correlation-Id").Single();
    }

    [Fact]
    public async Task Valid_correlation_id_is_echoed() => Assert.Equal("abc-123_x.y", await RoundTripAsync("abc-123_x.y"));

    [Theory]
    [InlineData("bad id with spaces")]
    [InlineData("<script>alert(1)</script>")]
    public async Task Invalid_correlation_id_is_replaced(string sent)
    {
        var returned = await RoundTripAsync(sent);

        Assert.NotEqual(sent, returned);
        Assert.Matches("^[A-Za-z0-9._-]{1,64}$", returned);
    }

    [Fact]
    public async Task Overlong_correlation_id_is_replaced()
    {
        var sent = new string('a', 65);

        Assert.NotEqual(sent, await RoundTripAsync(sent));
    }
}