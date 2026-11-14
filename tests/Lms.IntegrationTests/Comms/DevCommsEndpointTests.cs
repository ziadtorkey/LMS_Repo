using System.Net;
using System.Net.Http.Json;
using Lms.Api.Features.Dev;
using Lms.Domain.Comms;
using Lms.Infrastructure.Comms;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Notifications;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Comms;

/// <summary>M6 Task 18: the Development-only comms routes (fake channel listing, clear, scripted outcomes) and the runbook go-live list.</summary>
[Collection(ApiCollection.Name)]
public sealed class DevCommsEndpointTests(LmsApiFactory factory)
{
    private FakeDeliveryChannels Fakes => factory.Services.GetRequiredService<FakeDeliveryChannels>();

    private static HttpRequestMessage Request(HttpMethod method, string url, bool admin, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        if (admin)
        {
            request.Headers.Add(TestAuthHandler.AdminHeader, "true");
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Dev_comms_routes_are_404_outside_development(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        using var app = builder.Build();

        new CommsDevModule().MapEndpoints(app);

        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().Select(e => e.RoutePattern.RawText).ToList();
        Assert.DoesNotContain(Routes.DevCommsOutbox, routes);
        Assert.DoesNotContain(CommsDevModule.ScriptRoute, routes);
    }

    [Fact]
    public async Task Outbox_listing_shows_channel_template_and_masked_address_only()
    {
        Fakes.Reset();
        var user = Guid.NewGuid();
        await Fakes.Email.SendAsync(new DeliveryRequest(Guid.NewGuid(), Guid.NewGuid(), user, "student.one@example.test", NotificationEvents.GradesPublished, "{\"Secret\":\"x\"}", "/grades", "Grades.Published"), CancellationToken.None);

        var response = await factory.CreateClient().SendAsync(Request(HttpMethod.Get, Routes.DevCommsOutbox, admin: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("s***@e***.test", text);
        Assert.Contains("Grades.Published", text);
        Assert.DoesNotContain("student.one@example.test", text);
        Assert.DoesNotContain("Secret", text); // event parameters (message data) never appear
    }

    [Fact]
    public async Task Delete_clears_the_recorded_sends_and_script_queues_the_next_outcome()
    {
        Fakes.Reset();
        using var client = factory.CreateClient();

        var scripted = await client.SendAsync(Request(HttpMethod.Post, CommsDevModule.ScriptRoute, admin: true,
            new CommsDevModule.ScriptRequest("Email", "Transient", "smtp_4xx", 60)));
        var first = await Fakes.Email.SendAsync(new DeliveryRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "a@b.test", null, null, null, "T"), CancellationToken.None);
        var second = await Fakes.Email.SendAsync(new DeliveryRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "a@b.test", null, null, null, "T"), CancellationToken.None);
        var cleared = await client.SendAsync(Request(HttpMethod.Delete, Routes.DevCommsOutbox, admin: true));

        Assert.Equal(HttpStatusCode.NoContent, scripted.StatusCode);
        Assert.Equal((DeliveryOutcomeKind.Transient, "smtp_4xx", (TimeSpan?)TimeSpan.FromSeconds(60)), (first.Kind, first.ErrorCode, first.RetryAfter));
        Assert.Equal(DeliveryOutcomeKind.Accepted, second.Kind);
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        Assert.Empty(Fakes.Email.Sent);
    }

    [Theory]
    [InlineData("Sms", "Accepted", 1)]
    [InlineData("Email", "Exploded", 1)]
    [InlineData("Email", "Accepted", 0)]
    [InlineData("Email", "Accepted", 101)]
    public async Task A_bad_script_is_400(string channel, string outcome, int count)
    {
        var response = await factory.CreateClient().SendAsync(Request(HttpMethod.Post, CommsDevModule.ScriptRoute, admin: true,
            new CommsDevModule.ScriptRequest(channel, outcome, null, null, count)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Client_admins_are_403_and_anonymous_is_401()
    {
        var tree = await AccessTestData.SeedAsync(factory, "DC4");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        using var client = factory.CreateClient();

        var asClientAdmin = Request(HttpMethod.Get, Routes.DevCommsOutbox, admin: false);
        asClientAdmin.Headers.Remove(TestAuthHandler.UserHeader);
        asClientAdmin.Headers.Add(TestAuthHandler.UserHeader, admin.UserId.ToString());
        asClientAdmin.Headers.Add(ContextHeader.Name, admin.Id.ToString());

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(asClientAdmin)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Routes.DevCommsOutbox)).StatusCode);
    }

    [Fact]
    public void Runbook_go_live_list_names_d65_d67_d71_d73_and_says_no_sms()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "runbook-comms.md"));
        var goLive = text[text.IndexOf("## Go-live prerequisites", StringComparison.Ordinal)..];

        foreach (var decision in new[] { "D65", "D67", "D71", "D73" })
        {
            Assert.Contains(decision, goLive);
        }

        Assert.Contains("SMS is not in v1", text);
        Assert.Contains("D76", text);
        Assert.Contains("Deferred to v2: SMS", text);
        Assert.Contains("data region", goLive);
        Assert.Contains("SPF", text);
        Assert.Contains("DKIM", text);
        Assert.Contains("DMARC", text);
        Assert.Contains("VAPID", text);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LMS.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
