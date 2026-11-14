using System.Net;
using Lms.Client.Services.Api;
using Lms.Client.Services.Messaging;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Messaging;

namespace Lms.Client.Tests.Messaging;

/// <summary>The HTTP clients use the wave-1 contract routes, verbs and query strings (the announcement store is /api/comms/announcements, not the course board).</summary>
public sealed class MessagingHttpClientsTests
{
    private sealed class Recorder : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private static readonly Guid Id = Guid.NewGuid(), Other = Guid.NewGuid();

    private static (ApiHttp Http, Recorder Recorder) Make()
    {
        var recorder = new Recorder();
        return (new ApiHttp(new HttpClient(recorder) { BaseAddress = new Uri("http://localhost/") }, new InMemoryContextStore()), recorder);
    }

    private static async Task Ignore(Task task)
    {
        try { await task; }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException) { } // an empty body: only the request matters
    }

    [Fact]
    public async Task Messaging_client_routes()
    {
        var (http, recorder) = Make();
        var api = new MessagingApi(http);

        await Ignore(api.ListConversationsAsync(2, 20));
        await Ignore(api.GetMessagesAsync(Id, new MessageQuery(Other, 30)));
        await Ignore(api.SendAsync(Id, new SendMessageRequest("hi", null)));
        await Ignore(api.DeleteMessageAsync(Id, Other));
        await Ignore(api.MarkReadAsync(Id));
        await Ignore(api.SearchAsync("a b", Id));
        await Ignore(api.ReportAsync(Other, new ReportMessageRequest(ReportReasons.Spam, null)));
        await Ignore(api.BlockAsync(Other));
        await Ignore(api.UnblockAsync(Other));
        await Ignore(api.GetRecipientsAsync("sar"));
        await Ignore(api.GetUnreadTotalAsync());

        Assert.Contains((HttpMethod.Get, $"{Routes.Conversations}?page=2&pageSize=20"), recorder.Requests);
        Assert.Contains((HttpMethod.Get, Routes.ConversationMessages(Id, Other, 30)), recorder.Requests);
        Assert.Contains((HttpMethod.Post, Routes.ConversationMessages(Id)), recorder.Requests);
        Assert.Contains((HttpMethod.Delete, Routes.Message(Id, Other)), recorder.Requests);
        Assert.Contains((HttpMethod.Post, Routes.ConversationRead(Id)), recorder.Requests);
        Assert.Contains((HttpMethod.Get, Routes.MessageSearchUrl("a b", Id)), recorder.Requests);
        Assert.Contains((HttpMethod.Post, Routes.MessageReport(Other)), recorder.Requests);
        Assert.Contains((HttpMethod.Put, Routes.MyBlock(Other)), recorder.Requests);
        Assert.Contains((HttpMethod.Delete, Routes.MyBlock(Other)), recorder.Requests);
        Assert.Contains((HttpMethod.Get, Routes.MessagingRecipientsSearch("sar")), recorder.Requests);
        Assert.Contains((HttpMethod.Get, Routes.MyUnreadTotal), recorder.Requests);
    }

    [Fact]
    public async Task Announcements_client_uses_the_comms_store_not_the_course_board()
    {
        var (http, recorder) = Make();
        var api = new AnnouncementsApi(http);

        await Ignore(api.ListAsync(new AnnouncementQuery(AnnouncementStatuses.Draft, 1, 50)));
        await Ignore(api.PublishAsync(Id));
        await Ignore(api.WithdrawAsync(Id));
        await Ignore(api.DeleteAsync(Id));
        await Ignore(api.PreviewAudienceAsync(new AudiencePreviewRequest(Other, [], null)));
        await Ignore(api.ListMineAsync());
        await Ignore(api.MarkReadAsync(Id));

        Assert.Contains((HttpMethod.Get, "/api/comms/announcements?page=1&pageSize=50&status=Draft"), recorder.Requests);
        Assert.Contains((HttpMethod.Post, $"/api/comms/announcements/{Id}/publish"), recorder.Requests);
        Assert.Contains((HttpMethod.Post, $"/api/comms/announcements/{Id}/withdraw"), recorder.Requests);
        Assert.Contains((HttpMethod.Delete, $"/api/comms/announcements/{Id}"), recorder.Requests);
        Assert.Contains((HttpMethod.Post, "/api/comms/announcements/audience-preview"), recorder.Requests);
        Assert.Contains((HttpMethod.Get, $"{Routes.MyAnnouncements}?page=1&pageSize={Paging.DefaultPageSize}"), recorder.Requests);
        Assert.Contains((HttpMethod.Post, Routes.MyAnnouncementRead(Id)), recorder.Requests);
        Assert.DoesNotContain(recorder.Requests, r => r.Path.StartsWith("/api/announcements", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Comms_client_routes()
    {
        var (http, recorder) = Make();
        var api = new CommsApi(http);

        await Ignore(api.GetEffectivePreferencesAsync());
        await Ignore(api.SavePreferencesAsync(new SavePreferencesRequest([])));
        await Ignore(api.SendTestEmailAsync());
        await Ignore(api.RemovePushSubscriptionAsync(Id));
        await Ignore(api.GetMessagingRulesAsync());
        await Ignore(api.SaveMessagingRulesAsync(new SaveMessagingRulesRequest([])));
        await Ignore(api.ListReportsAsync(new ReportQuery(ReportStatuses.Open)));
        await Ignore(api.ResolveReportAsync(Id, new ResolveReportRequest(ModerationActions.Dismiss, null, null)));

        Assert.Contains((HttpMethod.Get, Routes.MyEffectivePreferences), recorder.Requests);
        Assert.Contains((HttpMethod.Put, Routes.MyNotificationPreferences), recorder.Requests);
        Assert.Contains((HttpMethod.Post, Routes.MyEmailTest), recorder.Requests);
        Assert.Contains((HttpMethod.Delete, Routes.MyPushSubscription(Id)), recorder.Requests);
        Assert.Contains((HttpMethod.Get, Routes.CommsMessagingRules), recorder.Requests);
        Assert.Contains((HttpMethod.Put, Routes.CommsMessagingRules), recorder.Requests);
        Assert.Contains((HttpMethod.Get, $"{Routes.ModerationReports}?page=1&pageSize={Paging.DefaultPageSize}&status=Open"), recorder.Requests);
        Assert.Contains((HttpMethod.Post, Routes.ModerationReportResolve(Id)), recorder.Requests);
    }

    [Fact]
    public async Task The_course_page_never_asks_the_personal_announcement_feed()
    {
        var (http, recorder) = Make();
        var api = new AnnouncementsApi(http);

        var items = await api.ListForCourseAsync(Id);

        Assert.Empty(items); // course board only until the dedicated route exists
        Assert.DoesNotContain(recorder.Requests, r => r.Path.StartsWith("/api/me/announcements", StringComparison.Ordinal)); // it has no course filter: it would list everything
    }
}
