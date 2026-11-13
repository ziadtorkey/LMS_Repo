using System.Globalization;
using Bunit;
using Lms.Client.Components.Editors;
using Lms.Client.Pages.Forums;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Forums;

public sealed class ForumTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;
    private readonly FakeForumsApi _forum = new();

    public ForumTests()
    {
        Services.AddSingleton<IForumsApi>(_forum);
        Services.AddSingleton<IAssetsApi, FakeAssetsApi>();
        Culture.Current = new CultureInfo("en");
    }

    private async Task<IRenderedComponent<ForumPage>> OpenForumAsync(params string[] permissions)
    {
        await GrantAsync(permissions);
        RenderProviders();
        var page = Render<ForumPage>(p => p.Add(x => x.Id, Cc));
        page.WaitForElement("[data-testid=forum-threads], [data-testid=forum-empty], [data-testid=forum-forbidden]", Wait);
        return page;
    }

    private async Task<IRenderedComponent<ThreadPage>> OpenThreadAsync(Guid threadId, params string[] permissions)
    {
        await GrantAsync(permissions);
        RenderProviders();
        var page = Render<ThreadPage>(p => p.Add(x => x.Id, Cc).Add(x => x.ThreadId, threadId));
        page.WaitForElement("[data-testid=thread-title], [data-testid=thread-error], [data-testid=thread-forbidden]", Wait);
        return page;
    }

    private static Task SetEditorAsync(IRenderedComponent<ThreadPage> page, string html) =>
        page.InvokeAsync(() => page.FindComponent<RichTextEditor>().Instance.ValueChanged.InvokeAsync(html));

    [Fact]
    public async Task Pinned_threads_first()
    {
        var old = _forum.AddThread(Cc, "Old", activity: DateTime.UtcNow.AddDays(-3));
        var recent = _forum.AddThread(Cc, "Recent", activity: DateTime.UtcNow);
        var pinned = _forum.AddThread(Cc, "Pinned but quiet", pinned: true, activity: DateTime.UtcNow.AddDays(-30));

        var page = await OpenForumAsync(Permissions.Forums.View);

        var order = page.FindAll("[data-testid^=forum-thread-]:not([data-testid^=forum-thread-link-])")
            .Select(e => e.GetAttribute("data-testid")!["forum-thread-".Length..]).ToList();
        Assert.Equal([pinned.Id.ToString(), recent.Id.ToString(), old.Id.ToString()], order);
        Assert.NotNull(page.Find($"[data-testid=forum-pinned-{pinned.Id}]"));
    }

    [Fact]
    public async Task Locked_threads_show_a_badge_and_reply_counts()
    {
        var t = _forum.AddThread(Cc, "Closed", locked: true);
        _forum.Threads[0] = t with { ReplyCount = 4 };

        var page = await OpenForumAsync(Permissions.Forums.View);

        Assert.NotNull(page.Find($"[data-testid=forum-locked-{t.Id}]"));
        Assert.Contains("4 replies", page.Find($"[data-testid=forum-thread-{t.Id}]").TextContent);
    }

    [Fact]
    public async Task New_thread_button_needs_Forums_Post()
    {
        var student = await OpenForumAsync(Permissions.Forums.View);
        Assert.Empty(student.FindAll("[data-testid=forum-new]"));
    }

    [Fact]
    public async Task Without_Forums_View_the_forum_is_forbidden()
    {
        var page = await OpenForumAsync();
        Assert.NotNull(page.Find("[data-testid=forum-forbidden]"));
        Assert.Empty(page.FindAll("[data-testid=tab-forum]"));
    }

    [Fact]
    public async Task Locked_thread_hides_reply_composer()
    {
        var t = _forum.AddThread(Cc, "Closed", locked: true);

        var page = await OpenThreadAsync(t.Id, Permissions.Forums.View, Permissions.Forums.Post);

        Assert.Empty(page.FindAll("[data-testid=thread-reply]"));
        Assert.NotNull(page.Find("[data-testid=thread-locked-note]"));
    }

    [Fact]
    public async Task Open_thread_shows_the_reply_composer_and_posts_a_reply()
    {
        var t = _forum.AddThread(Cc, "Open");
        var page = await OpenThreadAsync(t.Id, Permissions.Forums.View, Permissions.Forums.Post);

        await SetEditorAsync(page, "<p>My reply</p>");
        page.Find("[data-testid=thread-reply-send]").Click();

        page.WaitForAssertion(() => Assert.Equal("<p>My reply</p>", Assert.Single(_forum.PostRequests).BodyHtml));
        page.WaitForAssertion(() => Assert.Contains("My reply", page.Markup));
    }

    [Fact]
    public async Task Moderator_sees_delete_lock_pin()
    {
        _forum.Moderator = true;
        var t = _forum.AddThread(Cc, "Topic");
        var page = await OpenThreadAsync(t.Id, Permissions.Forums.View, Permissions.Forums.Post, Permissions.Forums.Moderate);

        page.Find("[data-testid=thread-moderate] button").Click();

        Popovers.WaitForAssertion(() =>
        {
            Assert.NotNull(Popovers.Find("[data-testid=thread-pin]"));
            Assert.NotNull(Popovers.Find("[data-testid=thread-lock]"));
            Assert.NotNull(Popovers.Find("[data-testid=thread-delete]"));
        });
        Popovers.Find("[data-testid=thread-lock]").Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid=thread-locked]")));
        Assert.True(_forum.Threads.Single().IsLocked);
    }

    [Fact]
    public async Task Student_cannot_see_moderation()
    {
        var t = _forum.AddThread(Cc, "Topic");
        var page = await OpenThreadAsync(t.Id, Permissions.Forums.View, Permissions.Forums.Post);

        Assert.Empty(page.FindAll("[data-testid=thread-moderate]"));
        Assert.Empty(page.FindAll("[data-testid^=post-delete-]"));
    }

    [Fact]
    public async Task Announcement_notify_toggle_sent()
    {
        _forum.Moderator = true;
        await GrantAsync(Permissions.Forums.View, Permissions.Forums.Moderate);
        RenderProviders();
        var page = Render<AnnouncementsPage>(p => p.Add(x => x.Id, Cc));
        page.WaitForElement("[data-testid=announcement-form]", Wait);

        page.Find("[data-testid=announcement-title]").Input("Exam moved");
        await page.InvokeAsync(() => page.FindComponent<RichTextEditor>().Instance.ValueChanged.InvokeAsync("<p>Friday</p>"));
        await page.InvokeAsync(() => page.FindComponents<MudSwitch<bool>>().Last().Instance.ValueChanged.InvokeAsync(true));
        page.Find("[data-testid=announcement-send]").Click();

        page.WaitForAssertion(() => Assert.Single(_forum.AnnouncementRequests));
        var sent = _forum.AnnouncementRequests.Single();
        Assert.Equal(("Exam moved", "<p>Friday</p>", true), (sent.Title, sent.BodyHtml, sent.Notify));
    }

    [Fact]
    public async Task Students_read_announcements_without_a_form()
    {
        _forum.Announcements.Add(new(Guid.NewGuid(), Cc, "Welcome", "<p>Hi all</p>", true, Guid.NewGuid(), "م", "Teacher", DateTime.UtcNow, null));

        await GrantAsync(Permissions.Forums.View);
        RenderProviders();
        var page = Render<AnnouncementsPage>(p => p.Add(x => x.Id, Cc));

        page.WaitForElement("[data-testid=announcements-list]", Wait);
        Assert.Contains("Hi all", page.Markup);
        Assert.Empty(page.FindAll("[data-testid=announcement-form]"));
        Assert.Empty(page.FindAll("[data-testid^=announcement-delete-]"));
    }

    [Fact]
    public async Task Long_threads_page_beyond_the_first_page_of_replies()
    {
        _forum.PageSize = 2;
        var t = _forum.AddThread(Cc, "Long thread");
        for (var i = 1; i <= 5; i++)
            await _forum.CreatePostAsync(t.Id, new Lms.Shared.Forums.CreateForumPostRequest($"<p>reply {i}</p>", []));
        var page = await OpenThreadAsync(t.Id, Permissions.Forums.View, Permissions.Forums.Post);

        Assert.Equal(2, page.FindAll("[data-testid^=post-body-]").Count - 1); // the opening post + 2 replies
        page.Find("[data-testid=thread-more]").Click();
        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll("[data-testid^=post-body-]").Count - 1));
        page.Find("[data-testid=thread-more]").Click();

        page.WaitForAssertion(() => Assert.Equal(5, page.FindAll("[data-testid^=post-body-]").Count - 1));
        Assert.Empty(page.FindAll("[data-testid=thread-more]"));
        Assert.Contains("reply 5", page.Markup);
    }

    [Fact]
    public async Task A_new_reply_keeps_the_pages_already_loaded()
    {
        _forum.PageSize = 2;
        var t = _forum.AddThread(Cc, "Thread");
        for (var i = 1; i <= 3; i++)
            await _forum.CreatePostAsync(t.Id, new Lms.Shared.Forums.CreateForumPostRequest($"<p>old {i}</p>", []));
        var page = await OpenThreadAsync(t.Id, Permissions.Forums.View, Permissions.Forums.Post);
        page.Find("[data-testid=thread-more]").Click();
        page.WaitForAssertion(() => Assert.Contains("old 3", page.Markup));

        await SetEditorAsync(page, "<p>fresh</p>");
        page.Find("[data-testid=thread-reply-send]").Click();

        page.WaitForAssertion(() => Assert.Contains("fresh", page.Markup));
        Assert.Contains("old 3", page.Markup);
    }

    [Fact]
    public async Task Long_thread_lists_page_beyond_the_first_page()
    {
        _forum.PageSize = 2;
        for (var i = 1; i <= 5; i++) _forum.AddThread(Cc, $"Topic {i}", activity: DateTime.UtcNow.AddMinutes(-i));
        var page = await OpenForumAsync(Permissions.Forums.View);

        Assert.Equal(2, page.FindAll("[data-testid^=forum-thread-link-]").Count);
        page.Find("[data-testid=forum-more]").Click();
        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll("[data-testid^=forum-thread-link-]").Count));
        page.Find("[data-testid=forum-more]").Click();

        page.WaitForAssertion(() => Assert.Equal(5, page.FindAll("[data-testid^=forum-thread-link-]").Count));
        Assert.Empty(page.FindAll("[data-testid=forum-more]"));
    }

    [Fact]
    public async Task A_student_composer_has_no_attachment_uploader_and_does_not_ask_for_upload_limits()
    {
        var page = await OpenForumAsync(Permissions.Forums.View, Permissions.Forums.Post);
        page.Find("[data-testid=forum-new]").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=forum-new-composer]")));
        Assert.Empty(page.FindAll("[data-testid=asset-uploader]"));
    }

    [Fact]
    public async Task A_teacher_composer_offers_attachments()
    {
        var page = await OpenForumAsync(Permissions.Forums.View, Permissions.Forums.Post, Permissions.Modules.Manage);
        page.Find("[data-testid=forum-new]").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=asset-uploader]")));
    }
}
