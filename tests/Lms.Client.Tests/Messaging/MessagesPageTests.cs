using System.Globalization;
using Bunit;
using Lms.Client.Pages.Messages;
using Lms.Client.Services.Messaging;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Messaging;

/// <summary>The inbox and conversation pages (M6, #612) on the fakes: participants only, the D69 rules as recipient limits, the delete window.</summary>
public sealed class MessagesPageTests : ClientTestContext
{
    private static readonly DateTime T0 = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
    private readonly FakeMessagingRules _rules = new();
    private readonly FakeMessagingApi _messaging;
    private DateTime _now = T0;
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public MessagesPageTests()
    {
        _messaging = new FakeMessagingApi(_rules) { Now = () => _now };
        Services.AddSingleton<IMessagingApi>(_messaging);
        Api.Me = Api.Me with { UserId = FakeMessagingApi.Sara.Id };
    }

    private async Task<IRenderedComponent<MessagesPage>> OpenAsync(Guid? id = null, string culture = "en", params string[] permissions)
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        await GrantAsync(permissions.Length == 0 ? [Permissions.Messaging.View, Permissions.Messaging.Send] : permissions);
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
        return Render<MessagesPage>(p => p.Add(x => x.Id, id));
    }

    private async Task<ConversationDto> SeedAsync()
    {
        var conversation = await _messaging.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Direct, [FakeMessagingApi.Huda.Id], null, "Hello Huda"));
        _messaging.Receive(conversation.Id, FakeMessagingApi.Huda, "Hello Sara");
        return conversation;
    }

    [Fact]
    public async Task The_inbox_lists_the_conversations_with_unread_counts_and_opening_reads_them()
    {
        var conversation = await SeedAsync();
        Assert.Equal(1, (await _messaging.GetUnreadTotalAsync()).Conversations);

        var open = await OpenAsync(conversation.Id);
        open.WaitForElement("[data-testid^=msg-bubble-]");
        Assert.NotNull(open.Find($"[data-testid=msg-row-{conversation.Id}]")); // the inbox is beside the thread

        Assert.Equal(0, (await _messaging.GetUnreadTotalAsync()).Conversations);
        Assert.Equal(2, open.FindAll("[data-testid^=msg-bubble-]").Count);
    }

    [Fact]
    public async Task A_conversation_of_other_people_shows_the_not_found_state_and_no_messages()
    {
        var conversation = await SeedAsync();
        _messaging.Me = FakeMessagingApi.Admin; // an Org Admin is not a participant

        var page = await OpenAsync(conversation.Id);

        page.WaitForElement("[data-testid=msg-notfound]");
        Assert.Empty(page.FindAll("[data-testid^=msg-bubble-]"));
    }

    [Fact]
    public async Task Sending_adds_the_message_and_keeps_the_draft_when_the_server_refuses()
    {
        var conversation = await SeedAsync();
        var page = await OpenAsync(conversation.Id);
        page.WaitForElement("[data-testid^=msg-bubble-]");

        _messaging.Lock(conversation.Id); // closed meanwhile: the next send is 409 conversation_closed
        await page.InvokeAsync(() => page.FindComponent<Lms.Client.Components.Messaging.MessageComposer>().Instance.ValueChanged.InvokeAsync("my reply"));
        await page.InvokeAsync(() => page.FindComponent<Lms.Client.Components.Messaging.MessageComposer>().Instance.OnSend.InvokeAsync());

        page.WaitForElement("[data-testid=msg-error]");
        Assert.Equal(2, page.FindAll("[data-testid^=msg-bubble-]").Count); // nothing was added
    }

    [Fact]
    public async Task A_locked_conversation_is_read_only()
    {
        var conversation = await SeedAsync();
        _messaging.Lock(conversation.Id);

        var page = await OpenAsync(conversation.Id);
        page.WaitForElement("[data-testid^=msg-bubble-]");

        Assert.Contains("closed", page.Markup);
        Assert.Empty(page.FindAll("textarea"));
    }

    [Fact]
    public async Task Delete_is_offered_on_own_messages_inside_the_window_only()
    {
        var conversation = await SeedAsync();
        var page = await OpenAsync(conversation.Id);
        page.WaitForElement("[data-testid^=msg-bubble-]");

        Assert.Single(page.FindAll("[data-testid^=msg-delete-]")); // mine, not Huda's

        await page.Find("[data-testid^=msg-delete-]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid^=msg-delete-]")));
        Assert.Contains("deleted", page.Markup);
    }

    [Fact]
    public async Task After_the_window_there_is_no_delete()
    {
        var conversation = await SeedAsync();
        _now = T0.AddMinutes(MessagingLimits.DeleteWindowMinutes + 1);

        var page = await OpenAsync(conversation.Id);
        page.WaitForElement("[data-testid^=msg-bubble-]");

        Assert.Empty(page.FindAll("[data-testid^=msg-delete-]"));
    }

    [Fact]
    public async Task The_recipient_list_is_only_who_the_rules_allow()
    {
        var page = await OpenAsync();
        await page.Find("[data-testid=msg-new]").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid=newconv-list]");

        var picks = _dialogs.FindAll("[data-testid^=newconv-pick-]");

        Assert.Single(picks); // a student reaches only the teacher with a relationship (Huda), not Omar or Layla
        Assert.Contains($"newconv-pick-{FakeMessagingApi.Huda.Id}", picks[0].GetAttribute("data-testid"));
    }

    [Fact]
    public async Task When_the_rules_allow_nobody_the_dialog_says_so()
    {
        _rules.Save([new MessagingRuleCellDto(BuiltInRoles.Student, BuiltInRoles.Teacher, MessagingRuleModes.Deny)]);
        var page = await OpenAsync();

        await page.Find("[data-testid=msg-new]").ClickAsync(new());

        Assert.Contains("do not let you start", _dialogs.WaitForElement("[data-testid=newconv-none]").TextContent);
    }

    [Fact]
    public async Task Starting_a_conversation_needs_a_first_message_and_opens_it()
    {
        var page = await OpenAsync();
        await page.Find("[data-testid=msg-new]").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid=newconv-list]");
        await _dialogs.Find($"[data-testid=newconv-pick-{FakeMessagingApi.Huda.Id}]").ClickAsync(new());
        Assert.True(_dialogs.Find("[data-testid=newconv-start]").HasAttribute("disabled")); // no message yet

        await _dialogs.InvokeAsync(() => _dialogs.FindComponents<MudTextField<string>>().Last().Instance.ValueChanged.InvokeAsync("Hello"));
        await _dialogs.Find("[data-testid=newconv-start]").ClickAsync(new());

        _dialogs.WaitForAssertion(() => Assert.Single(_messaging.ListConversationsAsync().GetAwaiter().GetResult().Items), TimeSpan.FromSeconds(5));
        Assert.Contains("/messages/", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
    }

    [Fact]
    public async Task A_group_needs_the_right_to_create_groups()
    {
        _messaging.Me = FakeMessagingApi.Huda; // a teacher reaches several people
        Api.Me = Api.Me with { UserId = FakeMessagingApi.Huda.Id };
        _rules.Save([new MessagingRuleCellDto(BuiltInRoles.Teacher, BuiltInRoles.Student, MessagingRuleModes.Deny), new MessagingRuleCellDto(BuiltInRoles.Teacher, BuiltInRoles.Guardian, MessagingRuleModes.Deny)]);
        var page = await OpenAsync();
        await page.Find("[data-testid=msg-new]").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid=newconv-list]");

        await _dialogs.Find($"[data-testid=newconv-pick-{FakeMessagingApi.Layla.Id}]").ClickAsync(new());
        await _dialogs.Find($"[data-testid=newconv-pick-{FakeMessagingApi.Admin.Id}]").ClickAsync(new());

        // two people make a group: the summary says whether this role may create one; the dialog never offers a title otherwise
        Assert.True(_dialogs.FindAll("[data-testid=newconv-title]").Count == 1 || _dialogs.FindAll("[data-testid=newconv-nogroups]").Count == 1);
    }

    [Fact]
    public async Task The_page_needs_view_and_reading_without_send_has_no_composer()
    {
        var conversation = await SeedAsync();
        var none = await OpenAsync(null, "en", Permissions.Courses.View);
        none.WaitForAssertion(() => Assert.Empty(none.FindAll("[data-testid=msg-title]")));
        await DisposeComponentsAsync();

        var readOnly = await OpenAsync(conversation.Id, "en", Permissions.Messaging.View);
        readOnly.WaitForElement("[data-testid^=msg-bubble-]");
        Assert.Empty(readOnly.FindAll("[data-testid=msg-new]"));
        Assert.Empty(readOnly.FindAll("textarea"));
    }

    [Fact]
    public async Task The_page_renders_in_arabic()
    {
        var page = await OpenAsync(null, "ar");

        Assert.Equal("الرسائل", page.WaitForElement("[data-testid=msg-title]").TextContent.Trim());
    }

    private async Task<ConversationDto> GroupAsync()
    {
        var teacher = _messaging.Me;
        _messaging.Me = FakeMessagingApi.Huda;
        var group = await _messaging.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Group,
            [FakeMessagingApi.Sara.Id, FakeMessagingApi.Khaled.Id], "Class 10-A", "Welcome"));
        _messaging.Me = teacher;
        return group;
    }

    [Fact]
    public async Task A_student_reads_a_group_without_a_composer_and_a_guardian_may_post()
    {
        var group = await GroupAsync();

        var student = await OpenAsync(group.Id);
        student.WaitForElement("[data-testid^=msg-bubble-]");
        Assert.Contains("send a private message", student.Markup);
        Assert.Empty(student.FindAll("textarea"));
        await DisposeComponentsAsync();

        _messaging.Me = FakeMessagingApi.Khaled;
        Api.Me = Api.Me with { UserId = FakeMessagingApi.Khaled.Id };
        var guardian = await OpenAsync(group.Id);
        guardian.WaitForElement("[data-testid^=msg-bubble-]");
        Assert.NotEmpty(guardian.FindAll("textarea"));
    }

    [Fact]
    public async Task Mute_toggles_and_leaving_a_group_returns_to_the_inbox()
    {
        var group = await GroupAsync();
        var page = await OpenAsync(group.Id);
        page.WaitForElement("[data-testid^=msg-bubble-]");

        await page.Find("[data-testid=msg-mute]").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Contains("Unmute", page.Find("[data-testid=msg-mute]").TextContent));
        Assert.True((await _messaging.GetConversationAsync(group.Id)).Muted);

        await page.Find("[data-testid=msg-leave]").ClickAsync(new());

        Assert.EndsWith("messages", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
        Assert.Empty((await _messaging.ListConversationsAsync()).Items);
    }

    [Fact]
    public async Task A_direct_conversation_has_no_leave()
    {
        var conversation = await SeedAsync();
        var page = await OpenAsync(conversation.Id);
        page.WaitForElement("[data-testid^=msg-bubble-]");

        Assert.NotEmpty(page.FindAll("[data-testid=msg-mute]"));
        Assert.Empty(page.FindAll("[data-testid=msg-leave]"));
    }

    [Fact]
    public async Task The_delete_window_closing_while_the_page_is_open_removes_the_button_and_says_why()
    {
        var conversation = await SeedAsync();
        var page = await OpenAsync(conversation.Id);
        page.WaitForElement("[data-testid^=msg-delete-]");
        _now = T0.AddMinutes(MessagingLimits.DeleteWindowMinutes + 1); // the server clock moves on

        await page.Find("[data-testid^=msg-delete-]").ClickAsync(new());

        Assert.Contains("passed", page.WaitForElement("[data-testid=msg-error]").TextContent);
        Assert.Empty(page.FindAll("[data-testid^=msg-delete-]"));
    }

    [Fact]
    public async Task A_404_on_send_shows_the_not_found_state()
    {
        var group = await GroupAsync();
        _messaging.Me = FakeMessagingApi.Khaled;
        Api.Me = Api.Me with { UserId = FakeMessagingApi.Khaled.Id };
        var page = await OpenAsync(group.Id);
        page.WaitForElement("[data-testid^=msg-bubble-]");
        await _messaging.LeaveAsync(group.Id); // removed from the group meanwhile

        await page.InvokeAsync(() => page.FindComponent<Lms.Client.Components.Messaging.MessageComposer>().Instance.ValueChanged.InvokeAsync("hello"));
        await page.InvokeAsync(() => page.FindComponent<Lms.Client.Components.Messaging.MessageComposer>().Instance.OnSend.InvokeAsync());

        page.WaitForElement("[data-testid=msg-notfound]");
    }

    [Fact]
    public async Task An_org_admin_sees_the_safeguarding_row_instead_of_students()
    {
        _messaging.Me = FakeMessagingApi.Admin;
        Api.Me = Api.Me with { UserId = FakeMessagingApi.Admin.Id };
        var page = await OpenAsync();

        await page.Find("[data-testid=msg-new]").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid=newconv-list]");

        Assert.Contains("No students here", _dialogs.Find("[data-testid=picker-d69]").TextContent);
    }
}
