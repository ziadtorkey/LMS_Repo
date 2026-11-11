using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Identity;
using Lms.Domain.Notifications;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Access;
using Lms.Shared.Authorization;
using Lms.Shared.Notifications;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Assignments;

[Collection(ApiCollection.Name)]
public sealed class AssignmentNotificationTests
{
    private readonly LmsApiFactory _factory;
    private readonly WebApplicationFactory<Program> _host;

    public AssignmentNotificationTests(LmsApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IEmailSender>(new FakeEmailSender())));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, RoleAssignment caller, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, caller.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, caller.Id.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _host.CreateClient().SendAsync(request);
    }

    private async Task<List<Notification>> RowsAsync(Guid userId)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Notification>().Where(n => n.UserId == userId).OrderBy(n => n.CreatedAt).ToListAsync();
    }

    private async Task<User> CreateUserAsync()
    {
        var email = $"n-{Guid.NewGuid():N}@lms.test";
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var user = new User { UserName = email, Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayNameAr = "مستخدم", DisplayNameEn = "User" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Assignee_gets_role_assigned_notification()
    {
        var tree = await AccessTestData.SeedAsync(_factory, "AN1");
        var admin = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var user = await CreateUserAsync();
        var teacher = tree.Roles[BuiltInRoles.Teacher];

        var response = await SendAsync(HttpMethod.Post, Routes.Assignments, admin,
            new CreateAssignmentRequest(user.Email!, null, null, teacher.Id, tree.School1.Id, null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var row = Assert.Single(await RowsAsync(user.Id));
        Assert.Equal(NotificationEvents.AccessRoleAssigned, row.EventKey);
        Assert.Equal(tree.Org.Id, row.OrganizationId);
        Assert.StartsWith("/", row.DeepLink);
        var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(row.ParametersJson)!;
        Assert.Equal(teacher.NameEn, parameters["RoleNameEn"]);
        Assert.Equal(teacher.NameAr, parameters["RoleNameAr"]);
        Assert.Equal(tree.School1.NameEn, parameters["OrgUnitNameEn"]);
        Assert.Equal(tree.School1.NameAr, parameters["OrgUnitNameAr"]);
    }

    [Fact]
    public async Task Assignee_gets_role_removed_notification()
    {
        var tree = await AccessTestData.SeedAsync(_factory, "AN2");
        var admin = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var user = await CreateUserAsync();
        var assigned = await AccessTestData.AssignAsync(_factory, tree, tree.School2, BuiltInRoles.Teacher, user.Id);

        var response = await SendAsync(HttpMethod.Delete, Routes.Assignment(assigned.Id), admin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = Assert.Single(await RowsAsync(user.Id));
        Assert.Equal(NotificationEvents.AccessRoleRemoved, row.EventKey);
        Assert.Equal(tree.Org.Id, row.OrganizationId);
        var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(row.ParametersJson)!;
        Assert.Equal(tree.School2.NameEn, parameters["OrgUnitNameEn"]);
        Assert.Equal(tree.Roles[BuiltInRoles.Teacher].NameEn, parameters["RoleNameEn"]);
    }

    [Fact]
    public async Task Refused_assignment_sends_no_notification()
    {
        var tree = await AccessTestData.SeedAsync(_factory, "AN3");
        var manager = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());
        var user = await CreateUserAsync();

        var response = await SendAsync(HttpMethod.Post, Routes.Assignments, manager,
            new CreateAssignmentRequest(user.Email!, null, null, tree.Roles[BuiltInRoles.OrgAdmin].Id, tree.School1.Id, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await RowsAsync(user.Id));
    }
}
