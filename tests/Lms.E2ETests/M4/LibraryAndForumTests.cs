using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M4;

/// <summary>
/// M4a exit demo 3 (plan Task 33, #383): the library and the course forum, in Arabic (RTL) and English. The school, the class course
/// with its published plan, the seeded teacher and two enrolled students come from <see cref="M4Kit"/> (API); what a scenario tests is
/// driven through the UI: an administrator adds a book with downloading off, the teacher adds it to a lesson, a student reads it
/// without a download; a student starts a thread and the teacher pins and locks it; the teacher publishes a pinned announcement and
/// the students are notified.
/// </summary>
/// <remarks>
/// The seeded Academic Supervisor is scoped to the Primary stage, a sibling of the Kindergarten class used here, so a supervisor's
/// resource would sit outside the teacher's tree (D13: a resource is visible at and below its owner unit). The Org Admin, who manages
/// the library at the school, adds the book instead; the supervisor's rights are covered by the integration probes.
/// </remarks>
public sealed class LibraryAndForumTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    /// <summary>Un-skip when the M4a library (#430, #443, #397), forums (#424, #398) and module pages (#391, #390, #394) are on main.</summary>
    private const string NeedsM4a = "Needs the M4a library and forum APIs and pages, and the module API and student course home, on main";

    private static readonly byte[] Pdf = "%PDF-1.7\n%probe\n"u8.ToArray();

    [Theory(Skip = NeedsM4a)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Supervisor_adds_library_book_with_download_disabled_teacher_adds_it_to_lesson_student_reads_without_download(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000); // the WASM app takes a while to boot after a full page load
        var suffix = M4Names.Suffix();
        var title = $"Book {suffix}";
        var titleAr = $"كتاب {suffix}";
        var world = await SetUpAsync(suffix);
        var shownTitle = culture == "ar" ? titleAr : title;

        // The administrator adds a book (a PDF) to the school's library with downloading turned off.
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        await new ShellPage(Page).WaitReadyAsync();
        await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        var library = new LibraryPage(Page);
        await library.OpenAsync();
        var form = await library.NewAsync();
        await form.PickRootUnitAsync();
        await form.SetTitlesAsync(titleAr, title);
        await form.UploadFileAsync("book.pdf", "application/pdf", Pdf);
        await form.Download.ClickAsync(); // on by default; this turns it off
        await form.SaveAsync();
        await Expect(library.Card(shownTitle)).ToBeVisibleAsync();
        var resourceId = await ResourceIdAsync(library, shownTitle);

        // The teacher finds it (visible below its owner, D13) and adds it to the first lesson of the class course.
        await M4Kit.SignOutAsync(Page);
        await M4Kit.SignInAsync(Page, world.TeacherEmail, Settings.Password, culture);
        await library.OpenAsync();
        await Expect(library.Card(shownTitle)).ToBeVisibleAsync();
        var add = await library.AddToLessonAsync(shownTitle);
        await add.AddAsync(culture == "ar" ? $"مادة {suffix}" : $"Course {suffix}", culture == "ar" ? "الدرس ١" : "Lesson 1");

        // The student sees the module in the lesson, opens the book in the reader, and has no way to download it.
        await M4Kit.SignOutAsync(Page);
        await M4Kit.SignInAsync(Page, world.Student1.UserName, world.Student1.Password, culture);
        var course = new MyCoursePage(Page);
        await course.OpenAsync(world.ClassCourseId);
        await Expect(course.ModuleLinks.Filter(new() { HasText = shownTitle })).ToHaveCountAsync(1);

        await library.OpenAsync();
        var reader = await library.ReadAsync(shownTitle);
        await Expect(reader.Root).ToBeVisibleAsync();
        await Expect(reader.Download).ToHaveCountAsync(0);
        Assert.Equal(403, (await new E2EApi(Page, Settings.BaseUrl).SendAsync("GET", $"/api/library/{resourceId}/download")).Status);
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    [Theory(Skip = NeedsM4a)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Student_posts_thread_teacher_pins_and_locks_it(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var suffix = M4Names.Suffix();
        var topic = $"Question {suffix}";
        var world = await SetUpAsync(suffix);

        // A student starts a thread; it shows in the list without pin or lock.
        await M4Kit.SignInAsync(Page, world.Student1.UserName, world.Student1.Password, culture);
        var forum = new ForumPage(Page);
        await forum.OpenAsync(world.ClassCourseId);
        await forum.StartThreadAsync(topic, "How do I start?");
        await Expect(forum.Pinned(topic)).ToHaveCountAsync(0);
        await Expect(forum.Locked(topic)).ToHaveCountAsync(0);
        var threadId = await ThreadIdAsync(forum, topic);

        // The teacher pins and locks it.
        await M4Kit.SignOutAsync(Page);
        await M4Kit.SignInAsync(Page, world.TeacherEmail, Settings.Password, culture);
        await forum.OpenAsync(world.ClassCourseId);
        await forum.ThreadLink(topic).ClickAsync();
        var thread = new ThreadPage(Page);
        await thread.WaitReadyAsync();
        await thread.TogglePinAsync();
        await Expect(thread.PinnedChip).ToBeVisibleAsync();
        await thread.ToggleLockAsync();
        await Expect(thread.LockedChip).ToBeVisibleAsync();

        // The student sees the pin and the lock, cannot reply, and the server refuses a reply sent around the page.
        await M4Kit.SignOutAsync(Page);
        await M4Kit.SignInAsync(Page, world.Student1.UserName, world.Student1.Password, culture);
        await forum.OpenAsync(world.ClassCourseId);
        await Expect(forum.Pinned(topic)).ToBeVisibleAsync();
        await Expect(forum.Locked(topic)).ToBeVisibleAsync();
        await forum.ThreadLink(topic).ClickAsync();
        await thread.WaitReadyAsync();
        await Expect(thread.LockedNote).ToBeVisibleAsync();
        await Expect(thread.ReplyComposer).ToHaveCountAsync(0);
        var reply = await new E2EApi(Page, Settings.BaseUrl).SendAsync(
            "POST", $"/api/forum-threads/{threadId}/posts", new { bodyHtml = "<p>one more</p>", assetIds = Array.Empty<Guid>() });
        Assert.Equal(409, reply.Status);
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    [Theory(Skip = NeedsM4a)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Teacher_posts_pinned_announcement_and_students_are_notified(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var suffix = M4Names.Suffix();
        var headline = $"Trip {suffix}";
        var world = await SetUpAsync(suffix);

        await M4Kit.SignInAsync(Page, world.TeacherEmail, Settings.Password, culture);
        var announcements = new AnnouncementsPage(Page);
        await announcements.OpenAsync(world.ClassCourseId);
        await announcements.PublishAsync(headline, "Bring a packed lunch.", pin: true, notify: true);
        await Expect(announcements.PinnedChip(headline)).ToBeVisibleAsync();

        // Both enrolled students are notified and see the announcement pinned.
        foreach (var student in new[] { world.Student1, world.Student2 })
        {
            await M4Kit.SignOutAsync(Page);
            await M4Kit.SignInAsync(Page, student.UserName, student.Password, culture);
            await Page.GetByTestId("notif-toggle").ClickAsync();
            var item = Page.Locator("[data-testid^='notif-item-']").Filter(new() { HasText = headline });
            await Expect(item).ToHaveCountAsync(1);
            await item.ClickAsync();
            await Expect(announcements.PinnedChip(headline)).ToBeVisibleAsync();
            await Expect(announcements.Form).ToHaveCountAsync(0); // students read, they do not publish
        }

        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    [Theory(Skip = NeedsM4a)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Nav_Library(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var world = await SetUpAsync(M4Names.Suffix());

        // Every role that can read the library has the entry in the menu and reaches the page through it.
        foreach (var (user, password) in new[] { (world.TeacherEmail, Settings.Password), (world.Student1.UserName, world.Student1.Password) })
        {
            await M4Kit.SignInAsync(Page, user, password, culture);
            var shell = new ShellPage(Page);
            if (!await shell.Nav("library").IsVisibleAsync())
            {
                await shell.NavToggle.ClickAsync();
            }

            await Expect(shell.Nav("library")).ToBeVisibleAsync();
            await shell.Nav("library").ClickAsync();
            await Page.WaitForURLAsync(url => url.Contains("/library", StringComparison.Ordinal));
            await Expect(Page.Locator("[data-testid='library-grid'], [data-testid='library-empty']").First).ToBeVisibleAsync();
            await M4Kit.SignOutAsync(Page);
        }
    }

    /// <summary>Signs in as the Org Admin, builds the world through the API, then clears the session.</summary>
    private async Task<M4World> SetUpAsync(string suffix)
    {
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        await new ShellPage(Page).WaitReadyAsync();
        await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        var world = await M4Kit.BuildWorldAsync(new ApiSetup(Page), suffix);
        await M4Kit.SignOutAsync(Page);
        return world;
    }

    /// <summary>The id of a resource, read from its card's test id (<c>library-card-{id}</c>).</summary>
    private static async Task<Guid> ResourceIdAsync(LibraryPage library, string title) =>
        Guid.Parse((await library.Card(title).GetAttributeAsync("data-testid"))!["library-card-".Length..]);

    /// <summary>The id of a thread, read from its link's test id (<c>forum-thread-link-{id}</c>).</summary>
    private static async Task<Guid> ThreadIdAsync(ForumPage forum, string title) =>
        Guid.Parse((await forum.ThreadLink(title).GetAttributeAsync("data-testid"))!["forum-thread-link-".Length..]);
}
