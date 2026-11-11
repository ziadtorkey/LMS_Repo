using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Configuration;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Access;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Assignments;

/// <summary>QA 3's two Lows on #107: no 500 for an assignment whose user row is gone; invitations in the unit's culture.</summary>
[Collection(ApiCollection.Name)]
public sealed class AssignmentFollowUpTests
{
    private readonly LmsApiFactory _factory;
    private readonly WebApplicationFactory<Program> _host;
    private readonly FakeEmailSender _emails = new();

    public AssignmentFollowUpTests(LmsApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IEmailSender>(_emails)));
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, RoleAssignment caller, object? body = null, string? language = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, caller.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, caller.Id.ToString());
        if (language is not null)
        {
            request.Headers.Add("Accept-Language", language);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _host.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task Assignment_whose_user_row_is_missing_answers_404_not_500()
    {
        var tree = await AccessTestData.SeedAsync(_factory, "FU1");
        var admin = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var orphan = await AccessTestData.AssignAsync(_factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());

        var response = await SendAsync(HttpMethod.Put, Routes.Assignment(orphan.Id), admin, new UpdateAssignmentRequest(null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
