using System.Net;
using System.Text.Json;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Zoom;
using Microsoft.Extensions.Options;

namespace Lms.UnitTests.Zoom;

public sealed class ZoomMeetingProviderTests
{
    private static readonly DateTime Start = new(2027, 2, 3, 8, 30, 0, DateTimeKind.Utc);
    private static readonly Guid Org = Guid.NewGuid(), ClassCourse = Guid.NewGuid(), Session = Guid.NewGuid();

    private const string Created = """{"id":93455555555,"join_url":"https://zoom.test/j/93455555555","start_url":"https://zoom.test/s/93455555555?zak=SECRET"}""";

    private sealed class Slots : IHostSlotStore
    {
        public List<HostSlot> Booked { get; } = [];

        public Task<IReadOnlyList<HostSlot>> SlotsAroundAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<HostSlot>>([.. Booked.Where(s => s.StartUtc < toUtc && s.EndUtc > fromUtc)]);
    }

    private sealed class Rig(ZoomMeetingProvider provider, FakeZoomHandler api, Slots slots, List<string> reserved)
    {
        public ZoomMeetingProvider Provider { get; } = provider;
        public FakeZoomHandler Api { get; } = api;
        public Slots Slots { get; } = slots;
        public List<string> Reserved { get; } = reserved;
    }

    private static Rig Build(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> zoom, ZoomOptions? options = null, int share = 100)
    {
        options ??= new ZoomOptions { AccountId = "acct", ClientId = "cid", ClientSecret = "test-secret-value", HostPool = ["h1@pool.test", "h2@pool.test"] };
        var wrapped = Options.Create(options);
        var clock = new ManualClock(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var auth = new FakeZoomHandler((_, n) => Task.FromResult(FakeZoomHandler.Token("tok" + n)));
        var tokens = new ZoomTokenCache(new HttpClient(auth), wrapped, clock);
        var fake = new FakeZoomHandler(zoom);
        var http = new HttpClient(new ZoomApiHandler(tokens, new ZoomOutboundLimiter(clock), wrapped) { InnerHandler = fake })
        {
            BaseAddress = new Uri("https://api.zoom.test/v2/"),
        };
        var slots = new Slots();
        var allocator = new ZoomHostAllocator(wrapped, new InProcessJobKeyLock(), slots, () => share);
        var provider = new ZoomMeetingProvider(new ZoomApiClient(http), allocator, new ZoomProviderStatus(tokens, wrapped, clock), wrapped);
        return new Rig(provider, fake, slots, []);
    }

    private static MeetingRequest Request(Rig rig, string topic = "Algebra 3", bool cloud = false, string? host = null, bool reserve = true) =>
        new(Session, topic, Start, 45, "Africa/Cairo", Org, ClassCourse, cloud, host,
            reserve ? host2 =>
            {
                rig.Reserved.Add(host2);
                rig.Slots.Booked.Add(new HostSlot(host2, Org, Start, Start.AddMinutes(45)));
                return Task.CompletedTask;
            } : null);

    private static Task<HttpResponseMessage> Ok(string json = Created) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.Created, json));

    [Fact]
    public async Task Create_reserves_the_chosen_host_then_creates_the_meeting_under_it()
    {
        var rig = Build((_, _) => Ok());

        var result = await rig.Provider.CreateMeetingAsync(Request(rig));

        Assert.Equal("h1@pool.test", Assert.Single(rig.Reserved));
        Assert.Equal("93455555555", result.ExternalMeetingId);
        Assert.Equal("h1@pool.test", result.HostUserKey);
        Assert.Contains("zak=", result.StartUrl);
        Assert.EndsWith("users/h1%40pool.test/meetings", Assert.Single(rig.Api.Requests).Url);
    }

    [Fact]
    public async Task Create_sends_the_title_only_and_the_platform_tracking_ids()
    {
        var rig = Build((_, _) => Ok());

        await rig.Provider.CreateMeetingAsync(Request(rig, topic: "Algebra 3"));

        using var body = JsonDocument.Parse(rig.Api.Requests[0].Body!);
        Assert.Equal("Algebra 3", body.RootElement.GetProperty("topic").GetString());
        var tracking = body.RootElement.GetProperty("tracking_fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("field").GetString()!, f => f.GetProperty("value").GetString());
        Assert.Equal(Org.ToString(), tracking["client"]);
        Assert.Equal(ClassCourse.ToString(), tracking["classCourse"]);
    }

    [Theory]
    [InlineData(false, "none")]
    [InlineData(true, "cloud")]
    public async Task Cloud_recording_follows_the_request(bool cloud, string expected)
    {
        var rig = Build((_, _) => Ok());

        await rig.Provider.CreateMeetingAsync(Request(rig, cloud: cloud));

        using var body = JsonDocument.Parse(rig.Api.Requests[0].Body!);
        Assert.Equal(expected, body.RootElement.GetProperty("settings").GetProperty("auto_recording").GetString());
    }

    [Fact]
    public async Task A_retry_with_a_known_host_skips_the_allocation()
    {
        var rig = Build((_, _) => Ok());

        var result = await rig.Provider.CreateMeetingAsync(Request(rig, host: "h2@pool.test"));

        Assert.Empty(rig.Reserved);
        Assert.Equal("h2@pool.test", result.HostUserKey);
        Assert.EndsWith("users/h2%40pool.test/meetings", rig.Api.Requests[0].Url);
    }

    [Fact]
    public async Task An_exhausted_pool_is_capacity_exhausted_and_nothing_is_created()
    {
        var rig = Build((_, _) => Ok());
        rig.Slots.Booked.AddRange([new HostSlot("h1@pool.test", Guid.NewGuid(), Start, Start.AddMinutes(60)), new HostSlot("h2@pool.test", Guid.NewGuid(), Start, Start.AddMinutes(60))]);

        var ex = await Assert.ThrowsAsync<MeetingProviderException>(() => rig.Provider.CreateMeetingAsync(Request(rig)));

        Assert.Equal(MeetingFailure.CapacityExhausted, ex.Failure);
        Assert.NotNull(ex.RetryAfterSeconds);
        Assert.Equal(0, rig.Api.Calls);
        Assert.Empty(rig.Reserved);
    }

    [Fact]
    public async Task A_client_over_its_host_share_is_capacity_exhausted()
    {
        var rig = Build((_, _) => Ok(), share: 50); // one of two hosts per client
        rig.Slots.Booked.Add(new HostSlot("h1@pool.test", Org, Start, Start.AddMinutes(60)));

        var ex = await Assert.ThrowsAsync<MeetingProviderException>(() => rig.Provider.CreateMeetingAsync(Request(rig)));

        Assert.Equal(MeetingFailure.CapacityExhausted, ex.Failure);
    }

    [Fact]
    public async Task Without_credentials_it_is_not_configured_and_never_calls_zoom()
    {
        var rig = Build((_, _) => Ok(), new ZoomOptions());

        var ex = await Assert.ThrowsAsync<MeetingProviderException>(() => rig.Provider.CreateMeetingAsync(Request(rig)));

        Assert.Equal(MeetingFailure.NotConfigured, ex.Failure);
        Assert.Equal(0, rig.Api.Calls);
    }

    [Fact]
    public async Task A_zoom_failure_after_the_slot_was_reserved_is_failed_and_keeps_the_reservation()
    {
        var rig = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.BadGateway, "{}")));

        var ex = await Assert.ThrowsAsync<MeetingProviderException>(() => rig.Provider.CreateMeetingAsync(Request(rig)));

        Assert.Equal(MeetingFailure.Failed, ex.Failure);
        Assert.Single(rig.Reserved); // the row keeps its host, so the sweep retries under the same host
    }

    [Fact]
    public async Task Zoom_rate_limiting_is_capacity_exhausted_with_the_retry_hint()
    {
        var rig = Build((_, _) =>
        {
            var response = FakeZoomHandler.Json((HttpStatusCode)429, "{}");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(600));
            return Task.FromResult(response);
        });

        var ex = await Assert.ThrowsAsync<MeetingProviderException>(() => rig.Provider.CreateMeetingAsync(Request(rig, host: "h1@pool.test")));

        Assert.Equal(MeetingFailure.CapacityExhausted, ex.Failure);
        Assert.Equal(600, ex.RetryAfterSeconds);
    }

    [Fact]
    public async Task Failures_never_carry_a_response_body_or_a_credential()
    {
        var rig = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.BadRequest, "{\"message\":\"secret-detail test-secret-value\"}")));

        var ex = await Assert.ThrowsAsync<MeetingProviderException>(() => rig.Provider.CreateMeetingAsync(Request(rig)));

        Assert.DoesNotContain("secret-detail", ex.ToString());
        Assert.DoesNotContain("test-secret-value", ex.ToString());
    }

    [Fact]
    public async Task Update_patches_the_meeting_and_delete_removes_it()
    {
        var rig = Build((r, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));

        await rig.Provider.UpdateMeetingAsync("123", Request(rig, host: "h1@pool.test"));
        await rig.Provider.DeleteMeetingAsync("123");

        Assert.Equal(HttpMethod.Patch, rig.Api.Requests[0].Method);
        Assert.Equal(HttpMethod.Delete, rig.Api.Requests[1].Method);
    }

    [Fact]
    public async Task Participants_are_mapped_and_an_email_counts_only_for_a_signed_in_zoom_user()
    {
        const string json = """
            {"participants":[
              {"id":"zoomuser1","name":"Sara Student","user_email":"sara@school.test","join_time":"2027-02-03T08:31:00Z","leave_time":"2027-02-03T09:10:00Z","duration":2340},
              {"id":"","name":"Guest typing sara@school.test","user_email":"sara@school.test","join_time":"2027-02-03T08:32:00Z","leave_time":"2027-02-03T09:00:00Z","duration":1680}
            ],"next_page_token":""}
            """;
        var rig = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.OK, json)));

        var list = await rig.Provider.GetParticipantsAsync("123");

        Assert.Equal(2, list.Count);
        Assert.Equal("sara@school.test", list[0].Email);
        Assert.Null(list[1].Email); // the guest typed it: Zoom has no account behind it
        Assert.Equal(2340, list[0].DurationSeconds);
        Assert.Equal(new DateTime(2027, 2, 3, 8, 31, 0, DateTimeKind.Utc), list[0].JoinedAtUtc);
    }

    [Fact]
    public async Task Participants_follow_the_page_token_and_a_missing_report_is_empty()
    {
        var rig = Build((r, n) => Task.FromResult(n == 1
            ? FakeZoomHandler.Json(HttpStatusCode.OK, """{"participants":[{"id":"a","name":"A","user_email":"","join_time":"2027-02-03T08:31:00Z","leave_time":"2027-02-03T09:00:00Z","duration":60}],"next_page_token":"tok2"}""")
            : FakeZoomHandler.Json(HttpStatusCode.OK, """{"participants":[{"id":"b","name":"B","user_email":"","join_time":"2027-02-03T08:31:00Z","leave_time":"2027-02-03T09:00:00Z","duration":60}],"next_page_token":""}""")));

        var list = await rig.Provider.GetParticipantsAsync("123");

        Assert.Equal(["A", "B"], list.Select(p => p.DisplayName));
        Assert.Contains("next_page_token=tok2", rig.Api.Requests[1].Url);

        var missing = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.NotFound, "{}")));
        Assert.Empty(await missing.Provider.GetParticipantsAsync("123"));
    }

    [Fact]
    public async Task Recordings_list_only_completed_files_with_a_play_link()
    {
        const string json = """
            {"recording_files":[
              {"status":"completed","play_url":"https://zoom.test/rec/play/1","recording_type":"shared_screen_with_speaker_view"},
              {"status":"processing","play_url":"https://zoom.test/rec/play/2","recording_type":"audio_only"},
              {"status":"completed","play_url":"","recording_type":"chat_file"}
            ]}
            """;
        var rig = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.OK, json)));

        var list = await rig.Provider.GetRecordingsAsync("123");

        var one = Assert.Single(list);
        Assert.Equal("https://zoom.test/rec/play/1", one.Url);
        Assert.Equal("shared_screen_with_speaker_view", one.Kind);
    }

    [Fact]
    public async Task Health_reports_configured_and_whether_zoom_accepts_the_credentials()
    {
        var healthy = Build((_, _) => Ok());
        Assert.Equal(new ProviderHealth(true, true), await healthy.Provider.GetHealthAsync());

        var unconfigured = Build((_, _) => Ok(), new ZoomOptions());
        Assert.Equal(new ProviderHealth(false, false), await unconfigured.Provider.GetHealthAsync());
    }

    [Fact]
    public void The_provider_is_named_zoom() => Assert.Equal("Zoom", Build((_, _) => Ok()).Provider.Name);
}
