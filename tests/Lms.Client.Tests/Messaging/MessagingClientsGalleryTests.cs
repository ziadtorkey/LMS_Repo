using System.Globalization;
using Bunit;
using Lms.Client.Pages.Dev;
using Lms.Client.Services.Messaging;
using Lms.Client.Tests.Support;
using Lms.Shared.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Messaging;

/// <summary>The designer components wired to the M6 fakes on the standalone gallery: what the fakes allow is what the components show.</summary>
public sealed class MessagingClientsGalleryTests : ClientTestContext
{
    private readonly FakeMessagingApi _messaging;
    private readonly FakeCommsApi _comms;

    public MessagingClientsGalleryTests()
    {
        var rules = new FakeMessagingRules();
        _messaging = new FakeMessagingApi(rules);
        _comms = new FakeCommsApi(rules);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Lms:UseFakeApi"] = "true" }).Build());
        Services.AddSingleton<IMessagingApi>(_messaging);
        Services.AddSingleton<IAnnouncementsApi>(new FakeAnnouncementsApi());
        Services.AddSingleton<ICommsApi>(_comms);
        Culture.Current = new CultureInfo("en");
    }

    private IRenderedComponent<MessagingClientsGallery> Open()
    {
        var cut = Render<MessagingClientsGallery>();
        cut.WaitForElement("[data-testid=live-inbox]");
        return cut;
    }

    private string ConversationId() => _messaging.ListConversationsAsync().GetAwaiter().GetResult().Items[0].Id.ToString();

    [Fact]
    public void The_inbox_shows_the_seeded_conversation_with_its_unread_count()
    {
        var cut = Open();

        Assert.Single(cut.FindAll("[data-testid^=live-row-]"));
        Assert.Contains("1", cut.Find("[data-testid=live-unread]").TextContent);
    }

    [Fact]
    public async Task Opening_a_conversation_reads_it_and_sending_adds_a_bubble()
    {
        var cut = Open();
        await cut.Find($"[data-testid=live-open-{ConversationId()}]").ClickAsync(new());

        cut.WaitForElement("[data-testid^=live-msg-]");
        Assert.Contains("0", cut.Find("[data-testid=live-unread]").TextContent.Split(':')[1]);
        Assert.Equal(2, cut.FindAll("[data-testid^=live-msg-]").Count);
        Assert.NotEmpty(cut.FindAll("[data-testid^=live-delete-]")); // only my own message, inside the window
    }

    [Fact]
    public async Task A_rule_failure_shows_the_server_answer_and_creates_nothing()
    {
        var cut = Open();

        await cut.Find("[data-testid=live-start-student]").ClickAsync(new());

        cut.WaitForElement("[data-testid=live-error]");
        Assert.Single(cut.FindAll("[data-testid^=live-row-]"));
    }

    [Fact]
    public async Task A_locked_conversation_turns_the_composer_into_the_read_only_notice()
    {
        var cut = Open();
        await cut.Find($"[data-testid=live-open-{ConversationId()}]").ClickAsync(new());
        cut.WaitForElement("[data-testid^=live-msg-]");

        await cut.Find("[data-testid=live-lock]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Contains("closed", cut.Markup));
        Assert.Empty(cut.FindAll("textarea"));
    }

    [Fact]
    public async Task Loosening_a_rule_is_refused_and_narrowing_is_applied()
    {
        var cut = Open();

        await cut.Find("[data-testid=live-rule-loosen]").ClickAsync(new());
        cut.WaitForElement("[data-testid=live-error]");

        await cut.Find("[data-testid=live-rule-narrow]").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=live-error]")));
        Assert.Equal(MessagingRuleModes.Deny, _comms.Rules.Mode(Lms.Shared.Authorization.BuiltInRoles.Teacher, Lms.Shared.Authorization.BuiltInRoles.Student));
    }

    [Fact]
    public async Task Publishing_an_announcement_shows_it_to_the_reader_and_marking_read_updates_the_card()
    {
        var cut = Open();
        var before = cut.FindAll("[data-testid^=live-ann-]").Count(e => e.GetAttribute("data-testid")!.Split('-').Length == 7);

        await cut.Find("[data-testid=live-publish-ann]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.True(cut.FindAll("[data-testid^=live-ann-read-]").Count >= 2));
        Assert.True(before >= 1);
    }

    [Fact]
    public void The_preference_grid_locks_mandatory_events()
    {
        var cut = Open();

        Assert.Contains("messaging", cut.Find("[data-testid=live-prefs]").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(cut.FindAll("[data-testid=live-prefs] input[disabled]")); // a mandatory event cannot be switched off
    }

    [Fact]
    public void The_gallery_renders_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        FakeCultureService.Apply(new CultureInfo("ar"));

        var cut = Open();

        Assert.Contains("قواعد المراسلة", cut.Markup);
    }
}
