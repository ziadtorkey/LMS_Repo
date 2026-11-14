using System.Net;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Messaging;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The pins and the probes that wait for them (M6 plan Task 31; QA1's review of #770, items 1, 5 and 6, and rows of the #762 checklist).
/// <list type="bullet">
/// <item><see cref="Every_pin_is_still_pending"/>: a pinned key that became live fails here until the pin is removed, so the probes that waited for it run.</item>
/// <item>Probes with a real body: <c>Cache-Control: private, no-store</c> on the API (#736) and the push endpoint host allow-list (#767).</item>
/// <item>Probes that cannot be written before their backend exists fail with a clear instruction the moment it does: the audit of the moderation list read (#760,
/// checklist 1.4) and the L9 refusal of an <c>Sms</c> row by the outbox writer (#750).</item>
/// </list>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M6PendingProbeTests(LmsApiFactory factory, ITestOutputHelper output)
{
    public static TheoryData<string> PinKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in M6Pending.Pins.Keys)
        {
            data.Add(key);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PinKeys))]
    public async Task Every_pin_is_still_pending(string key) =>
        Assert.False(await M6Pending.IsLiveAsync(factory, key),
            $"{key} is live now ({M6Pending.Pins[key]} is on this branch). Remove it from M6Pending.Pins in this PR so the probes that wait for it run, and run them.");

    [Fact]
    public async Task Every_api_response_is_private_and_no_store()
    {
        if (await M6Pending.SkipAsync(factory, output, M6Pending.NoStoreHeader))
        {
            return;
        }

        var w = await M6Kit.WorldAsync(factory);
        var urls = new List<string> { "/api/me" };
        if (await M6Pending.IsLiveAsync(factory, M6Pending.CreateConversation))
        {
            var id = await M6Kit.NewDirectAsync(w.World);
            urls.AddRange([Routes.Conversations, Routes.Conversation(id), Routes.ConversationMessages(id), Routes.MessageSearchUrl("hello"), Routes.MyUnreadTotal]);
        }

        foreach (var url in urls)
        {
            using var response = await w.SendAsync(M4aCaller.TeacherA, HttpMethod.Get, url);
            Assert.True(response.Headers.CacheControl is { NoStore: true, Private: true }, $"GET {url} answered Cache-Control '{response.Headers.CacheControl}', expected private, no-store.");
        }
    }

    /// <summary>A push subscription may only point at a known push service host (#767): an endpoint on any other host is refused, so the server never posts to an address a user chose.</summary>
    [Theory]
    [InlineData("https://evil.example.com/push/abc")]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc")] // not https
    [InlineData("https://169.254.169.254/latest/meta-data")] // a link-local address
    [InlineData("https://localhost:5132/push")]
    public async Task Push_subscription_endpoint_must_be_an_allowed_push_service(string endpoint)
    {
        if (await M6Pending.SkipAsync(factory, output, M6Pending.PushSubscribe))
        {
            return;
        }

        var w = await M6Kit.WorldAsync(factory);
        using var response = await w.SendAsync(M4aCaller.TeacherA, HttpMethod.Post, Routes.MyPushSubscriptions, new SavePushSubscriptionRequest(endpoint, "p256dh", "auth", "QA"));
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
            $"POST {Routes.MyPushSubscriptions} with endpoint {endpoint} answered {(int)response.StatusCode}; an endpoint outside the allow-list must be refused (400 or 422).");
    }

    /// <summary>Checklist row 1.4 (#762): a read of the moderation list is itself audited. The audit shape arrives with #760, so the probe is written then.</summary>
    [Fact]
    public async Task Reading_the_moderation_list_is_audited()
    {
        if (await M6Pending.SkipAsync(factory, output, M6Pending.ModerationList))
        {
            return;
        }

        Assert.Fail("#760 is on this branch: write this probe. As the Org Admin of client A, GET " + Routes.ModerationReports +
                    ", then expect one audit entry for that read (actor, time, no message text) in the audit log of the client; a sibling-unit admin's read must not appear in client A's log.");
    }

    /// <summary>L9: the outbox writer refuses an <c>Sms</c> row and the dispatcher never claims one. The writer arrives with #750.</summary>
    [Fact]
    public async Task The_outbox_writer_refuses_an_sms_row_and_the_dispatcher_never_claims_it()
    {
        if (await M6Pending.SkipAsync(factory, output, M6Pending.OutboxWriter))
        {
            return;
        }

        Assert.Fail("#750 is on this branch: write this probe. (1) call the writer with channel \"Sms\" and expect a refusal, no row; (2) insert an Sms row by hand into DeliveryOutbox " +
                    "(a due, pending one) and run one dispatcher pass: expect it never claimed (status and attempts unchanged); (3) for every publish path that exists (announcement normal and urgent, " +
                    "mandatory events, guardian absence notice, account email, moderation notice) assert the rows it made hold no Sms. Keep any public name of the refusal out of M6NoSmsProbeTests' scan " +
                    "or add it to its AllowedPublicNames with the reason.");
    }
}
