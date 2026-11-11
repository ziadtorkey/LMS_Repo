using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Me;

namespace Lms.IntegrationTests.IdentityProviders;

/// <summary>A session started through a client's provider is bound to that client (Review Focus 2).</summary>
[Collection(ApiCollection.Name)]
public sealed class ExternalSessionTests(LmsApiFactory factory)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private async Task<HttpResponseMessage> GetAsync(string url, Guid user, Guid? assignment, Guid? externalOrg)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(TestAuthHandler.UserHeader, user.ToString());
        if (assignment is { } id)
        {
            request.Headers.Add(ContextHeader.Name, id.ToString());
        }

        if (externalOrg is { } org)
        {
            request.Headers.Add(TestAuthHandler.ExternalOrgHeader, org.ToString());
        }

        return await factory.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task Session_from_client_A_provider_cannot_select_client_B_context_even_for_dual_member()
    {
        var a = await AccessTestData.SeedAsync(factory, "ES1A");
        var b = await AccessTestData.SeedAsync(factory, "ES1B");
        var user = Guid.NewGuid();
        var inA = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        var inB = await AccessTestData.AssignAsync(factory, b, b.School1, BuiltInRoles.Teacher, user);

        var own = await GetAsync(Routes.MyPermissions, user, inA.Id, a.Org.Id);
        var other = await GetAsync(Routes.MyPermissions, user, inB.Id, a.Org.Id);

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        Assert.Contains("OutOfScope", await other.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Me_lists_only_contexts_of_the_provider_client_for_an_external_session()
    {
        var a = await AccessTestData.SeedAsync(factory, "ES2A");
        var b = await AccessTestData.SeedAsync(factory, "ES2B");
        var user = Guid.NewGuid();
        var inA = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        await AccessTestData.AssignAsync(factory, b, b.School1, BuiltInRoles.Teacher, user);

        var external = (await (await GetAsync(Routes.Me, user, null, a.Org.Id)).Content.ReadFromJsonAsync<MeDto>(Web))!;
        var password = (await (await GetAsync(Routes.Me, user, null, null)).Content.ReadFromJsonAsync<MeDto>(Web))!;

        Assert.Equal([inA.Id], external.Contexts.Select(c => c.AssignmentId));
        Assert.Equal(2, password.Contexts.Count); // a normal (password) session is unchanged
    }
}
