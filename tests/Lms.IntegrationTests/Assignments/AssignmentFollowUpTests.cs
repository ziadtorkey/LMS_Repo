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

    private async Task<HttpResponseMessage> InviteAsync(AccessTree tree, RoleAssignment admin, string email, string? language) =>
        await SendAsync(HttpMethod.Post, Routes.Assignments, admin,
            new CreateAssignmentRequest(email, "اسم", "Name", tree.Roles[BuiltInRoles.Teacher].Id, tree.School1.Id, null, null),
            language);

    [Fact]
    public async Task Invitation_is_sent_in_the_default_culture_of_the_target_unit_not_the_admins()
    {
        var tree = await AccessTestData.SeedAsync(_factory, "FU2");
        var admin = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new OrgUnitSetting(tree.Org.Id, tree.School1.Id, tree.School1.Path, SettingKeys.UiDefaultCulture.Name, "\"en\"", false));
            await db.SaveChangesAsync();
        }

        var english = $"en-{Guid.NewGuid():N}@lms.test";
        var arabic = $"ar-{Guid.NewGuid():N}@lms.test";

        // The admin's browser asks for Arabic; School1 defaults to English.
        Assert.Equal(HttpStatusCode.Created, (await InviteAsync(tree, admin, english, "ar")).StatusCode);
        Assert.Equal("Your LMS account is ready", Assert.Single(_emails.To(english)).Subject);
        Assert.Contains("culture=en", Assert.Single(_emails.To(english)).HtmlBody);

        // School2 has no setting: the catalog default (ar) applies even though the admin's browser asks for English.
        var response = await SendAsync(HttpMethod.Post, Routes.Assignments, admin,
            new CreateAssignmentRequest(arabic, "اسم", "Name", tree.Roles[BuiltInRoles.Teacher].Id, tree.School2.Id, null, null), "en");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var mail = Assert.Single(_emails.To(arabic));
        Assert.NotEqual("Your LMS account is ready", mail.Subject);
        Assert.Contains("culture=ar", mail.HtmlBody);
    }
}
