using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Comms;
using Lms.Infrastructure.Comms;
using Lms.Infrastructure.Comms.Push;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Comms;

/// <summary>M6 Task 8: own-only subscriptions with protected keys, the push channel over the fake provider, and the isolation of the table.</summary>
[Collection(ApiCollection.Name)]
public sealed class PushChannelTests(LmsApiFactory factory)
{
    private FakePushProvider Provider => factory.Services.GetRequiredService<FakePushProvider>();

    private static SavePushSubscriptionRequest Subscription(string name, string label = "Chrome on Windows") =>
        new($"https://push.example.test/send/{name}", "BPublicKeyOfTheBrowser" + name, "AuthSecret" + name, label);

    private async Task<(AccessTree Tree, Guid User, Guid Assignment)> NewMemberAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);
        return (tree, user, assignment.Id);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, Guid user, Guid assignment, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, user.ToString());
        request.Headers.Add(ContextHeader.Name, assignment.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return factory.CreateClient().SendAsync(request);
    }

    private async Task<List<PushSubscriptionDto>> ListAsync(Guid user, Guid assignment) =>
        (await (await SendAsync(HttpMethod.Get, Routes.MyPushSubscriptions, user, assignment)).Content.ReadFromJsonAsync<List<PushSubscriptionDto>>())!;

    [Fact]
    public async Task Subscribe_stores_protected_keys_and_lists_only_own_rows()
    {
        var (tree, user, assignment) = await NewMemberAsync("PU1");
        var other = Guid.NewGuid();
        var otherAssignment = (await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, other)).Id;
        var request = Subscription("pu1-a");

        var created = await SendAsync(HttpMethod.Post, Routes.MyPushSubscriptions, user, assignment, request);
        await SendAsync(HttpMethod.Post, Routes.MyPushSubscriptions, other, otherAssignment, Subscription("pu1-b"));
        var listed = await ListAsync(user, assignment);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(["Chrome on Windows"], listed.Select(s => s.DeviceLabel));
        Assert.DoesNotContain("push.example.test", await created.Content.ReadAsStringAsync()); // the endpoint is a capability URL: never returned
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var row = await db.Set<PushSubscription>().SingleAsync(s => s.UserId == user);
        Assert.DoesNotContain(request.P256dh, row.ProtectedP256dh);
        Assert.DoesNotContain(request.Auth, row.ProtectedAuth);
        var protector = factory.Services.GetRequiredService<PushKeyProtector>();
        Assert.Equal((request.P256dh, request.Auth), (protector.Unprotect(row.ProtectedP256dh), protector.Unprotect(row.ProtectedAuth)));
    }

    [Fact]
    public async Task Saving_the_same_endpoint_again_replaces_the_row()
    {
        var (_, user, assignment) = await NewMemberAsync("PU2");
        var request = Subscription("pu2");

        await SendAsync(HttpMethod.Post, Routes.MyPushSubscriptions, user, assignment, request);
        await SendAsync(HttpMethod.Post, Routes.MyPushSubscriptions, user, assignment, request with { P256dh = "BRotatedKey" });

        Assert.Single(await ListAsync(user, assignment));
    }

    [Theory]
    [InlineData("http://push.example.test/insecure", "k", "a")]
    [InlineData("not a url", "k", "a")]
    [InlineData("https://push.example.test/x", "", "a")]
    [InlineData("https://push.example.test/x", "k", "")]
    public async Task Invalid_subscriptions_are_refused(string endpoint, string p256dh, string auth)
    {
        var (_, user, assignment) = await NewMemberAsync("PU3");

        var response = await SendAsync(HttpMethod.Post, Routes.MyPushSubscriptions, user, assignment, new SavePushSubscriptionRequest(endpoint, p256dh, auth, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Overlong_endpoint_is_refused()
    {
        var (_, user, assignment) = await NewMemberAsync("PU4");

        var response = await SendAsync(HttpMethod.Post, Routes.MyPushSubscriptions, user, assignment,
            new SavePushSubscriptionRequest("https://push.example.test/" + new string('a', PushSubscription.MaxEndpointLength), "k", "a", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Other_users_subscription_is_404_and_own_delete_removes_it()
    {
        var (tree, user, assignment) = await NewMemberAsync("PU5");
        var other = Guid.NewGuid();
        var otherAssignment = (await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, other)).Id;
        var mine = await (await SendAsync(HttpMethod.Post, Routes.MyPushSubscriptions, user, assignment, Subscription("pu5"))).Content.ReadFromJsonAsync<PushSubscriptionDto>();

        var foreign = await SendAsync(HttpMethod.Delete, Routes.MyPushSubscription(mine!.Id), other, otherAssignment);
        var own = await SendAsync(HttpMethod.Delete, Routes.MyPushSubscription(mine.Id), user, assignment);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
        Assert.Empty(await ListAsync(user, assignment));
    }

    [Fact]
    public async Task Anonymous_callers_are_refused_and_the_vapid_key_is_public_only()
    {
        var (_, user, assignment) = await NewMemberAsync("PU6");

        var anonymous = await factory.CreateClient().GetAsync(Routes.MyPushSubscriptions);
        var key = await SendAsync(HttpMethod.Get, Routes.MyVapidPublicKey, user, assignment);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, key.StatusCode);
        Assert.Equal(Provider.PublicKey, (await key.Content.ReadFromJsonAsync<VapidPublicKeyDto>())!.PublicKey);
    }

    private async Task<DeliveryOutcome> SendPushAsync(AccessTree tree, Guid user)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.For(tree.Org.Id, tree.Root.Path));
        var channel = new PushDeliveryChannel(db, Provider, factory.Services.GetRequiredService<PushKeyProtector>(), factory.Services.GetRequiredService<IClock>());
        return await channel.SendAsync(
            new DeliveryRequest(Guid.NewGuid(), tree.Org.Id, user, "push-subscription", NotificationEvents.GradesPublished, "{}", "/grades", null), CancellationToken.None);
    }

    [Fact]
    public async Task Push_goes_to_every_subscription_and_the_payload_has_ids_and_no_private_text()
    {
        Provider.Reset();
        var (tree, user, assignment) = await NewMemberAsync("PU7");
        await SendAsync(HttpMethod.Post, Routes.MyPushSubscriptions, user, assignment, Subscription("pu7-a"));
        await SendAsync(HttpMethod.Post, Routes.MyPushSubscriptions, user, assignment, Subscription("pu7-b"));

        var outcome = await SendPushAsync(tree, user);

        Assert.Equal(DeliveryOutcomeKind.Accepted, outcome.Kind);
        Assert.Equal(2, Provider.Sent.Count);
        var payload = Provider.Sent.First().Payload;
        var text = System.Text.Encoding.UTF8.GetString(payload);
        Assert.Contains("\"url\":\"/grades\"", text);
        Assert.Contains(NotificationEvents.GradesPublished, text);
        Assert.True(payload.Length <= PushPayloadBuilder.MaxBytes);
    }

    [Fact]
    public async Task Gone_endpoint_removes_the_subscription()
    {
        Provider.Reset();
        var (tree, user, assignment) = await NewMemberAsync("PU8");
        await SendAsync(HttpMethod.Post, Routes.MyPushSubscriptions, user, assignment, Subscription("pu8"));
        Provider.ScriptNext(new PushResult(PushResultKind.Gone, "push_gone"));

        var outcome = await SendPushAsync(tree, user);

        Assert.Equal(DeliveryOutcomeKind.Permanent, outcome.Kind);
        Assert.Empty(await ListAsync(user, assignment));
    }

    [Fact]
    public async Task Transient_failure_is_retried_with_the_push_service_retry_after_and_keeps_the_subscription()
    {
        Provider.Reset();
        var (tree, user, assignment) = await NewMemberAsync("PU9");
        await SendAsync(HttpMethod.Post, Routes.MyPushSubscriptions, user, assignment, Subscription("pu9"));
        Provider.ScriptNext(new PushResult(PushResultKind.Transient, "push_429", TimeSpan.FromMinutes(3)));

        var outcome = await SendPushAsync(tree, user);

        Assert.Equal((DeliveryOutcomeKind.Transient, "push_429", (TimeSpan?)TimeSpan.FromMinutes(3)), (outcome.Kind, outcome.ErrorCode, outcome.RetryAfter));
        Assert.Single(await ListAsync(user, assignment));
    }

    [Fact]
    public async Task A_user_with_a_subscription_resolves_a_push_contact_and_one_without_does_not()
    {
        var (tree, user, assignment) = await NewMemberAsync("PU10");
        await SendAsync(HttpMethod.Post, Routes.MyPushSubscriptions, user, assignment, Subscription("pu10"));
        var without = Guid.NewGuid();

        await using var db = factory.CreateDbContext(TestCurrentContext.For(tree.Org.Id, tree.Root.Path));
        var resolved = await new UserContactResolver(db).ResolveAsync([user, without], NotificationChannels.Push, CancellationToken.None);

        Assert.Equal([user], resolved.Keys);
    }

    [Fact]
    public async Task Subscriptions_of_client_B_are_invisible_in_client_A_context()
    {
        var a = await AccessTestData.SeedAsync(factory, "PU11A");
        var (b, userB, assignmentB) = await NewMemberAsync("PU11B");
        await SendAsync(HttpMethod.Post, Routes.MyPushSubscriptions, userB, assignmentB, Subscription("pu11"));

        await using var asA = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.Root.Path));
        await using var noClient = factory.CreateDbContext(new TestCurrentContext { UserId = userB });
        await using var admin = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());

        Assert.Empty(await asA.Set<PushSubscription>().ToListAsync());
        Assert.Empty(await noClient.Set<PushSubscription>().ToListAsync());
        Assert.Equal(b.Org.Id, (await admin.Set<PushSubscription>().SingleAsync(s => s.UserId == userB)).OrganizationId);
    }
}
