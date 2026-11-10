using System.Net;
using Lms.IntegrationTests.Infrastructure;

namespace Lms.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class HealthTests(LmsApiFactory factory)
{
    [Fact]
    public async Task Health_endpoint_returns_healthy()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}