using System.Net;
using System.Net.Http.Json;
using Bunit;
using Lms.Client.Services;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Me;
using Microsoft.AspNetCore.Components;

namespace Lms.Client.Tests.Services;

public sealed class LmsApiClientTests
{
    private sealed class CaptureHandler(HttpStatusCode status = HttpStatusCode.OK, object? body = null) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(body ?? new { }) });
        }
    }

    private sealed class TestNav : NavigationManager
    {
        public TestNav() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, NavigationOptions options) =>
            Uri = ToAbsoluteUri(uri).ToString();
    }

    private static (LmsApiClient Client, CaptureHandler Handler, InMemoryContextStore Store) Create(object? body = null)
    {
        var handler = new CaptureHandler(body: body);
        var store = new InMemoryContextStore();
        return (new LmsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }, store), handler, store);
    }

    [Fact]
    public async Task Api_client_adds_context_header_when_context_selected()
    {
        var (client, handler, store) = Create(new EffectivePermissionsDto(null, ["Roles.View"]));
        var id = Guid.NewGuid();
        await store.SetAsync(id);

        var result = await client.GetMyPermissionsAsync();

        Assert.Equal(Routes.MyPermissions, handler.Last!.RequestUri!.AbsolutePath);
        Assert.Equal(id.ToString(), handler.Last.Headers.GetValues(ContextHeader.Name).Single());
        Assert.Contains("Roles.View", result.Permissions);
    }

    [Fact]
    public async Task Api_client_omits_header_without_context()
    {
        var (client, handler, _) = Create(new MeDto(Guid.NewGuid(), "a", "b", false, []));

        await client.GetMeAsync();

        Assert.Equal(Routes.Me, handler.Last!.RequestUri!.AbsolutePath);
        Assert.False(handler.Last.Headers.Contains(ContextHeader.Name));
    }

    [Fact]
    public async Task Forbidden_response_redirects_to_forbidden_page()
    {
        var nav = new TestNav();
        var handler = new ForbiddenRedirectHandler(nav) { InnerHandler = new CaptureHandler(HttpStatusCode.Forbidden) };
        var response = await new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }.GetAsync("/api/x");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.EndsWith("/forbidden", nav.Uri);
    }

    [Fact]
    public async Task Client_throws_ApiForbiddenException_on_403()
    {
        var handler = new CaptureHandler(HttpStatusCode.Forbidden);
        var client = new LmsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }, new InMemoryContextStore());

        await Assert.ThrowsAsync<ApiForbiddenException>(() => client.GetMeAsync());
    }
}
