using System.Net;
using System.Text;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Microsoft.AspNetCore.Components;

namespace Lms.Client.Tests.Services;

/// <summary>
/// Bug #132: the redirect handler and the problem classifier both read the response body. These tests run the real
/// handler chain over a response whose content stream can be read only once (like a network stream), which the
/// bUnit fakes never did.
/// </summary>
public sealed class RealHandlerChainTests
{
    private sealed class SingleReadContent(string json) : HttpContent
    {
        private bool _read;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            if (_read) throw new ObjectDisposedException("response stream", "already read");
            _read = true;
            var bytes = Encoding.UTF8.GetBytes(json);
            return stream.WriteAsync(bytes, 0, bytes.Length);
        }

        protected override bool TryComputeLength(out long length) { length = -1; return false; }
    }

    private sealed class FakeNetwork(HttpStatusCode status, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = new SingleReadContent(json);
            content.Headers.ContentType = new("application/problem+json");
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }

    private sealed class TestNav : NavigationManager
    {
        public TestNav() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).ToString();
    }

    private static (ApiHttp Http, TestNav Nav, InMemoryContextStore Store) Chain(HttpStatusCode status, string json)
    {
        var nav = new TestNav();
        var store = new InMemoryContextStore();
        var handler = new ForbiddenRedirectHandler(nav, store) { InnerHandler = new FakeNetwork(status, json) };
        return (new ApiHttp(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }, store), nav, store);
    }

    [Fact]
    public async Task Post_403_policy_block_keeps_its_detail_through_the_real_handler()
    {
        var (http, nav, _) = Chain(HttpStatusCode.Forbidden, "{\"reason\":\"BlockedByPolicy\",\"detail\":\"Blocked by parent organization policy\"}");

        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => http.SendAsync(HttpMethod.Post, "/api/org-units", new { }));

        Assert.Equal("BlockedByPolicy", ex.Code);
        Assert.Equal("Blocked by parent organization policy", ex.Detail);
        Assert.DoesNotContain("/forbidden", nav.Uri);
    }

    [Fact]
    public async Task Post_403_permission_escalation_keeps_its_code()
    {
        var (http, _, _) = Chain(HttpStatusCode.Forbidden, "{\"code\":\"permission_escalation\"}");

        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => http.SendAsync(HttpMethod.Put, "/api/roles/x", new { }));

        Assert.Equal("permission_escalation", ex.Code);
    }

    [Fact]
    public async Task Get_403_goes_to_forbidden_page_without_an_object_disposed_error()
    {
        var (http, nav, _) = Chain(HttpStatusCode.Forbidden, "{\"reason\":\"MissingPermission\"}");

        await Assert.ThrowsAsync<ApiForbiddenException>(() => http.GetAsync<object>("/api/audit"));

        Assert.EndsWith("/forbidden", nav.Uri);
    }

    [Fact]
    public async Task Get_403_invalid_context_clears_context_and_throws_ApiContextException()
    {
        var (http, nav, store) = Chain(HttpStatusCode.Forbidden, "{\"code\":\"invalid_context\"}");
        await store.SetAsync(Guid.NewGuid());

        await Assert.ThrowsAsync<ApiContextException>(() => http.GetAsync<object>("/api/me/permissions"));

        Assert.Null(store.ActiveAssignmentId);
        Assert.EndsWith("/choose-context", nav.Uri);
    }

    [Fact]
    public async Task Other_problem_statuses_still_map_through_the_chain()
    {
        var (http, _, _) = Chain(HttpStatusCode.Conflict, "{\"code\":\"code_taken\",\"errors\":{\"Code\":[\"taken\"]}}");

        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => http.SendAsync(HttpMethod.Post, "/api/org-units", new { }));

        Assert.Equal("code_taken", ex.Code);
        Assert.Equal("taken", ex.Errors!["Code"].Single());
    }

    [Fact]
    public async Task LmsApiClient_403_through_the_real_chain_is_classified()
    {
        var nav = new TestNav();
        var store = new InMemoryContextStore();
        var handler = new ForbiddenRedirectHandler(nav, store) { InnerHandler = new FakeNetwork(HttpStatusCode.Forbidden, "{\"code\":\"context_required\"}") };
        var client = new LmsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }, store);

        await Assert.ThrowsAsync<ApiContextException>(() => client.GetMyPermissionsAsync());
    }
}
