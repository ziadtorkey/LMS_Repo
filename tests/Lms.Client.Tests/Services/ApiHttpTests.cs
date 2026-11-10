using System.Globalization;
using System.Net;
using System.Text;
using Bunit;
using Lms.Client.Resources;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Tests.Services;

public sealed class ApiHttpTests : ClientTestContext
{
    private sealed class StubHandler(HttpStatusCode status, string body = "{}", string mediaType = "application/problem+json") : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        public string? LastBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
        }
    }

    private ApiHttp Create(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }, Context);

    private IStringLocalizer<SharedResource> Loc => Services.GetRequiredService<IStringLocalizer<SharedResource>>();

    [Fact]
    public async Task Post_sends_json_body_and_context_header()
    {
        var id = Guid.NewGuid();
        await Context.SetAsync(id);
        var handler = new StubHandler(HttpStatusCode.OK, "{\"value\":3}", "application/json");

        var result = await Create(handler).SendAsync<Dictionary<string, int>>(HttpMethod.Post, "/api/x", new { name = "a" });

        Assert.Equal(3, result!["value"]);
        Assert.Contains("\"name\":\"a\"", handler.LastBody);
        Assert.Equal(id.ToString(), handler.Last!.Headers.GetValues("X-Lms-Context").Single());
    }

    [Fact]
    public async Task Problem_response_becomes_ApiProblemException_with_code_detail_and_errors()
    {
        var handler = new StubHandler(HttpStatusCode.Conflict,
            "{\"code\":\"code_taken\",\"detail\":\"d\",\"errors\":{\"Code\":[\"bad\"]}}");

        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => Create(handler).SendAsync(HttpMethod.Post, "/api/x", new { }));

        Assert.Equal(409, ex.Status);
        Assert.Equal("code_taken", ex.Code);
        Assert.Equal("d", ex.Detail);
        Assert.Equal("bad", ex.Errors!["Code"].Single());
    }

    [Fact]
    public async Task Post_403_does_not_redirect_but_throws_ApiProblemException()
    {
        var handler = new StubHandler(HttpStatusCode.Forbidden, "{\"reason\":\"BlockedByPolicy\",\"detail\":\"blocked\"}");

        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => Create(handler).SendAsync(HttpMethod.Post, "/api/x", new { }));

        Assert.Equal("BlockedByPolicy", ex.Code);
    }

    [Fact]
    public async Task Get_403_throws_ApiForbiddenException_for_the_forbidden_page()
    {
        var handler = new StubHandler(HttpStatusCode.Forbidden);
        await Assert.ThrowsAsync<ApiForbiddenException>(() => Create(handler).GetAsync<object>("/api/x"));
    }

    [Fact]
    public async Task Download_returns_stream_and_file_name()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "abc", "text/csv");
        var (content, name) = await Create(handler).DownloadAsync("/api/export");

        Assert.Equal("abc", await new StreamReader(content).ReadToEndAsync());
        Assert.Equal("download", name);
    }


    [Fact]
    public async Task Get_403_invalid_context_throws_ApiContextException_not_forbidden()
    {
        var handler = new StubHandler(HttpStatusCode.Forbidden, "{\"code\":\"invalid_context\"}");
        await Assert.ThrowsAsync<ApiContextException>(() => Create(handler).GetAsync<object>("/api/x"));
    }

    [Fact]
    public async Task Post_403_context_required_also_throws_ApiContextException()
    {
        var handler = new StubHandler(HttpStatusCode.Forbidden, "{\"code\":\"context_required\"}");
        await Assert.ThrowsAsync<ApiContextException>(() => Create(handler).SendAsync(HttpMethod.Post, "/api/x", new { }));
    }

    [Fact]
    public async Task Empty_success_body_returns_default()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "", "application/json");
        Assert.Null(await Create(handler).SendAsync<Dictionary<string, int>>(HttpMethod.Post, "/api/x", new { }));
    }

    [Theory]
    [InlineData("en", "MissingPermission", "You don't have permission to do this.")]
    [InlineData("ar", "MissingPermission", "ليست لديك صلاحية للقيام بذلك.")]
    [InlineData("en", "OutOfScope", "This item is outside your scope.")]
    public void Access_denial_reasons_have_their_own_messages(string culture, string code, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        Assert.Equal(expected, ProblemText.For(new ApiProblemException(403, code, null, null), Loc));
    }

    [Theory]
    [InlineData("ar", "هذا الرمز مستخدم بالفعل.")]
    [InlineData("en", "This code is already used.")]
    public void Problem_with_code_maps_to_Error_code_resource(string culture, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var ex = new ApiProblemException(409, "code_taken", null, null);
        Assert.Equal(expected, ProblemText.For(ex, Loc));
    }

    [Fact]
    public void Blocked_by_policy_shows_server_detail()
    {
        var ex = new ApiProblemException(403, "BlockedByPolicy", "Server says no", null);
        Assert.Equal("Server says no", ProblemText.For(ex, Loc));
    }

    [Fact]
    public void Unknown_code_falls_back_to_generic_message()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        var ex = new ApiProblemException(500, "something_new", null, null);
        Assert.Equal("Something went wrong. Try again.", ProblemText.For(ex, Loc));
    }
}
