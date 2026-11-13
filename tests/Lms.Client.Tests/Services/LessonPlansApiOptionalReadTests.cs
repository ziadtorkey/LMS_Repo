using System.Net;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Microsoft.AspNetCore.Components;

namespace Lms.Client.Tests.Services;

/// <summary>The decorative "my class courses" read of the Home cards must not send a role without LessonPlans.View to /forbidden.</summary>
public sealed class LessonPlansApiOptionalReadTests
{
    private sealed class Forbidden : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") });
    }

    private sealed class TestNav : NavigationManager
    {
        public TestNav() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).ToString();
    }

    [Fact]
    public async Task A_forbidden_read_is_an_empty_list_and_does_not_navigate_away()
    {
        var nav = new TestNav();
        var chain = new ForbiddenRedirectHandler(nav, new InMemoryContextStore()) { InnerHandler = new Forbidden() };
        var api = new LessonPlansApi(new ApiHttp(new HttpClient(chain) { BaseAddress = new Uri("http://localhost/") }, new InMemoryContextStore()));

        var courses = await api.GetMyClassCoursesIfAllowedAsync();

        Assert.Empty(courses);
        Assert.DoesNotContain("/forbidden", nav.Uri);
    }

    [Fact]
    public async Task The_strict_read_still_goes_to_the_forbidden_page()
    {
        var nav = new TestNav();
        var chain = new ForbiddenRedirectHandler(nav, new InMemoryContextStore()) { InnerHandler = new Forbidden() };
        var api = new LessonPlansApi(new ApiHttp(new HttpClient(chain) { BaseAddress = new Uri("http://localhost/") }, new InMemoryContextStore()));

        await Assert.ThrowsAsync<ApiForbiddenException>(() => api.GetMyClassCoursesAsync());
        Assert.EndsWith("/forbidden", nav.Uri);
    }
}
