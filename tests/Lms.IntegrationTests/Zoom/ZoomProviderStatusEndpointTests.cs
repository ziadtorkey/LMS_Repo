using System.Net;
using System.Net.Http.Json;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.LiveSessions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Lms.IntegrationTests.Zoom;

[Collection(ApiCollection.Name)]
public sealed class ZoomProviderStatusEndpointTests(LmsApiFactory factory) : IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _hosts = [];

    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private HttpClient Api(Dictionary<string, string?>? settings = null)
    {
        var host = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(settings ?? [])));
        _hosts.Add(host);
        return host.CreateClient();
    }

    private static HttpRequestMessage Get(bool platformAdmin, Guid? user = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Routes.LiveSessionsProviderStatus);
        if (platformAdmin)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
            request.Headers.Add(TestAuthHandler.AdminHeader, "true");
        }
        else if (user is not null)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, user.ToString());
        }

        return request;
    }

    [Fact]
    public async Task Anonymous_caller_gets_401()
    {
        var response = await Api().SendAsync(Get(platformAdmin: false));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_signed_in_user_without_the_permission_gets_403()
    {
        var response = await Api().SendAsync(Get(platformAdmin: false, user: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unconfigured_zoom_reports_not_configured_and_not_healthy()
    {
        var response = await Api().SendAsync(Get(platformAdmin: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ProviderStatusDto(false, false), await response.Content.ReadFromJsonAsync<ProviderStatusDto>());
    }

    [Fact]
    public async Task Configured_but_unreachable_zoom_is_reported_unhealthy_and_the_body_has_only_two_flags()
    {
        var api = Api(new()
        {
            ["Zoom:AccountId"] = "acct-test",
            ["Zoom:ClientId"] = "client-test",
            ["Zoom:ClientSecret"] = "secret-test-value",
            ["Zoom:HostPool:0"] = "host-1@pool.test",
            ["Zoom:TokenUrl"] = "http://127.0.0.1:1/oauth/token", // nothing listens here: the token call fails
        });

        var response = await api.SendAsync(Get(platformAdmin: true));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ProviderStatusDto(true, false), System.Text.Json.JsonSerializer.Deserialize<ProviderStatusDto>(
            body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        Assert.DoesNotContain("secret-test-value", body);
        Assert.DoesNotContain("acct-test", body);
        Assert.DoesNotContain("host-1", body);
    }
}
