using System.Net;
using System.Text.Json;
using Lms.Infrastructure.Zoom;

namespace Lms.UnitTests.Zoom;

public sealed class ZoomApiClientTests
{
    private static readonly DateTime Start = new(2027, 2, 3, 8, 30, 0, DateTimeKind.Utc);

    private static (ZoomApiClient Client, FakeZoomHandler Api) Build(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> api)
    {
        var fake = new FakeZoomHandler(api);
        var http = new HttpClient(fake) { BaseAddress = new Uri("https://api.zoom.test/v2/") };
        return (new ZoomApiClient(http), fake);
    }

    private static ZoomMeetingRequest Request(bool cloud = false) =>
        new("host-1@pool.test", "Algebra, lesson 3", Start, 45, "Africa/Cairo", cloud,
            new Dictionary<string, string> { ["client"] = "org-1", ["classCourse"] = "cc-1" });

    private const string Created = """{"id":93455555555,"join_url":"https://zoom.test/j/93455555555","start_url":"https://zoom.test/s/93455555555?zak=SECRET"}""";

    [Fact]
    public async Task Create_posts_the_fixed_meeting_settings_and_returns_ids_and_urls()
    {
        var (client, api) = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.Created, Created)));

        var meeting = await client.CreateMeetingAsync(Request());

        Assert.Equal("93455555555", meeting.MeetingId);
        Assert.Equal("https://zoom.test/j/93455555555", meeting.JoinUrl);
        Assert.StartsWith("https://zoom.test/s/", meeting.StartUrl);
        var call = Assert.Single(api.Requests);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.EndsWith("users/host-1%40pool.test/meetings", call.Url);
        using var body = JsonDocument.Parse(call.Body!);
        var root = body.RootElement;
        Assert.Equal(2, root.GetProperty("type").GetInt32());
        Assert.Equal("2027-02-03T08:30:00Z", root.GetProperty("start_time").GetString());
        Assert.Equal(45, root.GetProperty("duration").GetInt32());
        Assert.Equal("Africa/Cairo", root.GetProperty("timezone").GetString());
        var settings = root.GetProperty("settings");
        Assert.True(settings.GetProperty("waiting_room").GetBoolean());
        Assert.False(settings.GetProperty("join_before_host").GetBoolean());
    }

    [Fact]
    public async Task Topic_contains_only_the_session_title()
    {
        var (client, api) = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.Created, Created)));

        await client.CreateMeetingAsync(Request());

        using var body = JsonDocument.Parse(api.Requests[0].Body!);
        Assert.Equal("Algebra, lesson 3", body.RootElement.GetProperty("topic").GetString());
        // client and class course ids travel only as tracking fields, never in the topic or agenda
        Assert.False(body.RootElement.TryGetProperty("agenda", out _));
        Assert.DoesNotContain("org-1", body.RootElement.GetProperty("topic").GetString());
        var fields = body.RootElement.GetProperty("tracking_fields").EnumerateArray().Select(f => f.GetProperty("field").GetString()).ToList();
        Assert.Equal(["client", "classCourse"], fields);
    }

    [Theory]
    [InlineData(false, "none")]
    [InlineData(true, "cloud")]
    public async Task Cloud_recording_only_when_asked_for(bool cloud, string expected)
    {
        var (client, api) = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.Created, Created)));

        await client.CreateMeetingAsync(Request(cloud));

        using var body = JsonDocument.Parse(api.Requests[0].Body!);
        Assert.Equal(expected, body.RootElement.GetProperty("settings").GetProperty("auto_recording").GetString());
    }

    [Fact]
    public async Task A_topic_with_markup_or_over_the_limit_is_refused_before_any_call()
    {
        var (client, api) = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.Created, Created)));

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateMeetingAsync(Request() with { Topic = "<b>hi</b>" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateMeetingAsync(Request() with { Topic = new string('x', 201) }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateMeetingAsync(Request() with { Topic = "  " }));
        Assert.Equal(0, api.Calls);
    }

    [Fact]
    public async Task Update_patches_the_meeting_and_delete_tolerates_a_meeting_that_is_already_gone()
    {
        var (client, api) = Build((r, _) => Task.FromResult(r.Method == HttpMethod.Delete
            ? FakeZoomHandler.Json(HttpStatusCode.NotFound, """{"code":3001}""")
            : new HttpResponseMessage(HttpStatusCode.NoContent)));

        await client.UpdateMeetingAsync("123", Request());
        await client.DeleteMeetingAsync("123");

        Assert.Equal(HttpMethod.Patch, api.Requests[0].Method);
        Assert.EndsWith("meetings/123", api.Requests[0].Url);
        Assert.Equal(HttpMethod.Delete, api.Requests[1].Method);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_failed_call_is_a_provider_failure_without_the_response_body(HttpStatusCode status)
    {
        var (client, _) = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(status, """{"message":"secret-detail zak=SECRET"}""")));

        var ex = await Assert.ThrowsAsync<ZoomProviderException>(() => client.CreateMeetingAsync(Request()));

        Assert.Equal(ZoomFailure.Unavailable, ex.Failure);
        Assert.DoesNotContain("secret-detail", ex.ToString());
        Assert.Contains(((int)status).ToString(), ex.Message);
    }

    [Fact]
    public async Task A_response_without_the_expected_fields_is_a_provider_failure()
    {
        var (client, _) = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.Created, """{"id":1}""")));

        await Assert.ThrowsAsync<ZoomProviderException>(() => client.CreateMeetingAsync(Request()));
    }

    [Theory]
    [InlineData("""{"id":"u1","type":2,"status":"active"}""", true)]
    [InlineData("""{"id":"u1","type":1,"status":"active"}""", false)] // basic (unlicensed)
    [InlineData("""{"id":"u1","type":2,"status":"inactive"}""", false)]
    public async Task A_host_counts_as_licensed_only_when_active_with_a_licensed_type(string json, bool expected)
    {
        var (client, api) = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.OK, json)));

        Assert.Equal(expected, await client.IsLicensedHostAsync("host-1@pool.test"));
        Assert.EndsWith("users/host-1%40pool.test", api.Requests[0].Url);
    }

    [Fact]
    public async Task A_host_that_does_not_exist_is_not_licensed()
    {
        var (client, _) = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.NotFound, """{"code":1001}""")));

        Assert.False(await client.IsLicensedHostAsync("nobody@pool.test"));
    }
}
