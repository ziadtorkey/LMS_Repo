using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;

namespace Lms.IntegrationTests.Security;

/// <summary>Who calls an endpoint in the M1 probe matrix (M1 plan, Task 35).</summary>
public enum Caller
{
    /// <summary>Org Admin of client A, assigned at A's root.</summary>
    RootAdmin,

    /// <summary>Org Admin of client A, assigned at School1.</summary>
    School1Admin,

    /// <summary>Org Admin of client A, assigned at School2 (the sibling of School1).</summary>
    School2Admin,

    /// <summary>Teacher of client A at School1: few permissions.</summary>
    School1Teacher,

    /// <summary>Org Admin of client B, assigned at B's root (another client).</summary>
    ClientBAdmin,

    /// <summary>Platform admin without a client context.</summary>
    PlatformAdmin,

    /// <summary>No credentials at all.</summary>
    Anonymous,
}

/// <summary>
/// The world the M1 probes run in: client A (Root → School1, School2) and client B (Root), one assignment per caller.
/// Seeded once per test class through <see cref="InitializeAsync"/>; each instance uses fresh clients, so tests are
/// independent of other test classes that share the database.
/// </summary>
public sealed class M1Callers(LmsApiFactory factory)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<Caller, RoleAssignment> _assignments = [];
    private readonly Guid _platformAdminId = Guid.NewGuid();

    public LmsApiFactory Factory { get; } = factory;
    public AccessTree A { get; private set; } = null!;
    public AccessTree B { get; private set; } = null!;

    public async Task InitializeAsync(string name)
    {
        A = await AccessTestData.SeedAsync(Factory, name + "A");
        B = await AccessTestData.SeedAsync(Factory, name + "B");
        _assignments[Caller.RootAdmin] = await Assign(A, A.Root, BuiltInRoles.OrgAdmin);
        _assignments[Caller.School1Admin] = await Assign(A, A.School1, BuiltInRoles.OrgAdmin);
        _assignments[Caller.School2Admin] = await Assign(A, A.School2, BuiltInRoles.OrgAdmin);
        _assignments[Caller.School1Teacher] = await Assign(A, A.School1, BuiltInRoles.Teacher);
        _assignments[Caller.ClientBAdmin] = await Assign(B, B.Root, BuiltInRoles.OrgAdmin);
    }

    public RoleAssignment AssignmentOf(Caller caller) => _assignments[caller];

    /// <summary>Sends a request as the caller: test-auth user header plus the caller's context header.</summary>
    public Task<HttpResponseMessage> SendAsync(Caller caller, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        switch (caller)
        {
            case Caller.Anonymous:
                break;
            case Caller.PlatformAdmin:
                request.Headers.Add(TestAuthHandler.UserHeader, _platformAdminId.ToString());
                request.Headers.Add(TestAuthHandler.AdminHeader, "true");
                break;
            default:
                var assignment = _assignments[caller];
                request.Headers.Add(TestAuthHandler.UserHeader, assignment.UserId.ToString());
                request.Headers.Add(ContextHeader.Name, assignment.Id.ToString());
                break;
        }

        return Factory.CreateClient().SendAsync(request);
    }

    /// <summary>Sends as the user of any role assignment, in that assignment's context.</summary>
    public Task<HttpResponseMessage> SendAsAsync(RoleAssignment assignment, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        request.Headers.Add(TestAuthHandler.UserHeader, assignment.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, assignment.Id.ToString());
        return Factory.CreateClient().SendAsync(request);
    }

    /// <summary>Sends as the caller and returns the JSON body; fails when the status is not a success.</summary>
    public async Task<JsonElement> SendOkAsync(Caller caller, HttpMethod method, string url, object? body = null)
    {
        using var response = await SendAsync(caller, method, url, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"setup call {method} {url} as {caller} failed: {(int)response.StatusCode} {text}");
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private Task<RoleAssignment> Assign(AccessTree tree, Lms.Domain.Organizations.OrgUnit unit, string role) =>
        AccessTestData.AssignAsync(Factory, tree, unit, role, Guid.NewGuid());
}
