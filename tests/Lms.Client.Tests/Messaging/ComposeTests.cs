using Bunit;
using Lms.Client.Pages.Messaging;
using Lms.Client.Services.Messaging;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Messaging;

/// <summary>M6 Task 22: compose, the recipient picker, groups and their members (#612 C3), against the messaging fake.</summary>
public sealed class ComposeTests : ClientTestContext
{
    private static FakeMessagingUser Sara => FakeMessagingApi.Sara;
    private static FakeMessagingUser Huda => FakeMessagingApi.Huda;
    private static FakeMessagingUser Omar => FakeMessagingApi.Omar;
    private static FakeMessagingUser Khaled => FakeMessagingApi.Khaled;

    private DateTime _clock = DateTime.UtcNow.AddHours(-2);
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public ComposeTests() => MessagingApi.Now = () => _clock = _clock.AddSeconds(1);

    private async Task SignInAsync(FakeMessagingUser who)
    {
        Api.Me = Api.Me with { UserId = who.Id };
        await GrantAsync(Permissions.Messaging.View, Permissions.Messaging.Send);
        MessagingApi.Me = who;
    }

    private IRenderedComponent<MessagesPage> RenderPage(Guid? id = null)
    {
        _dialogs = Render<MudDialogProvider>();
        var cut = Render<MessagesPage>(p => p.Add(x => x.Id, id));
        cut.WaitForState(() => cut.FindAll("[data-testid=messages-loading]").Count == 0);
        return cut;
    }

    private IRenderedComponent<MessagesPage> OpenNew(Guid? id = null)
    {
        var cut = RenderPage(id);
        cut.Find("[data-testid=messages-new]").Click();
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=compose]")));
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=picker-list]")));
        return cut;
    }

    private static string[] PickerIds(IRenderedComponent<MudDialogProvider> d) =>
        [.. d.FindAll("[data-testid^=picker-]").Select(e => e.GetAttribute("data-testid")!).Where(t => Guid.TryParse(t["picker-".Length..], out _))];

    [Fact]
    public async Task Picker_shows_only_allowed_recipients()
    {
        await SignInAsync(Sara);
        OpenNew();

        var ids = PickerIds(_dialogs);
        Assert.Contains($"picker-{Huda.Id}", ids);
        Assert.DoesNotContain($"picker-{Omar.Id}", ids);
        Assert.DoesNotContain($"picker-{Khaled.Id}", ids);
    }

    [Fact]
    public async Task Picker_explains_the_rule_without_listing_forbidden_people()
    {
        await SignInAsync(Sara);
        OpenNew();

        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=picker-hint]")));
        var hint = _dialogs.Find("[data-testid=picker-hint]").TextContent;
        Assert.Contains("المعلمون", hint);
        Assert.DoesNotContain("عمر", _dialogs.Find("[data-testid=picker]").TextContent);
        Assert.DoesNotContain("خالد", _dialogs.Find("[data-testid=picker]").TextContent);
    }

    [Fact]
    public async Task Manager_picker_never_lists_students()
    {
        await SignInAsync(FakeMessagingApi.Admin);
        OpenNew();

        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=picker-d69]")));
        var ids = PickerIds(_dialogs);
        Assert.DoesNotContain($"picker-{Sara.Id}", ids);
        Assert.DoesNotContain($"picker-{Omar.Id}", ids);
    }

    [Fact]
    public async Task Student_has_no_new_group_action()
    {
        await SignInAsync(Sara);
        OpenNew();
        Assert.Empty(_dialogs.FindAll("[data-testid=compose-group]"));
    }

    [Fact]
    public async Task Staff_can_start_a_group_and_a_group_needs_two_people()
    {
        await SignInAsync(Huda);
        MessagingApi.Related.Add((Huda.Id, Omar.Id));
        OpenNew();

        _dialogs.Find("[data-testid=compose-group]").Click();

        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=compose-title]")));
        Assert.NotEmpty(_dialogs.FindAll("[data-testid=compose-min]"));
    }

    [Fact]
    public async Task Existing_one_to_one_is_opened_not_duplicated()
    {
        await SignInAsync(Sara);
        var first = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Direct, [Huda.Id], null, "hi"));
        var cut = OpenNew();

        _dialogs.Find($"[data-testid=picker-{Huda.Id}]").Click();
        _dialogs.Find("[data-testid=composer] textarea").Input("hello again");
        _dialogs.Find("[data-testid=composer-send]").Click();

        cut.WaitForAssertion(() => Assert.EndsWith($"/messages/{first.Id}", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri));
        Assert.Single((await MessagingApi.ListConversationsAsync()).Items);
    }

    [Fact]
    public async Task Rate_limited_keeps_the_draft_and_shows_try_again()
    {
        await SignInAsync(Sara);
        OpenNew();
        MessagingApi.RateLimited = true;

        _dialogs.Find($"[data-testid=picker-{Huda.Id}]").Click();
        _dialogs.Find("[data-testid=composer] textarea").Input("keep me");
        _dialogs.Find("[data-testid=composer-send]").Click();

        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=compose-error]")));
        Assert.Equal("keep me", _dialogs.Find("[data-testid=composer] textarea").GetAttribute("value"));
        Assert.False(_dialogs.Find("[data-testid=composer-send]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Ctrl_Enter_sends_Enter_breaks_the_line_and_Send_is_visible()
    {
        await SignInAsync(Sara);
        OpenNew();
        _dialogs.Find($"[data-testid=picker-{Huda.Id}]").Click();
        var box = _dialogs.Find("[data-testid=composer] textarea");
        box.Input("line one");

        box.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Empty((await MessagingApi.ListConversationsAsync()).Items); // Enter alone sends nothing
        Assert.NotEmpty(_dialogs.FindAll("[data-testid=composer-send]"));

        box.KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        _dialogs.WaitForAssertion(() => Assert.Single(MessagingApi.ListConversationsAsync().Result.Items));
    }

    [Fact]
    public async Task Deep_link_to_a_person_the_rules_deny_says_so_without_a_name()
    {
        await SignInAsync(Sara);
        _dialogs = Render<MudDialogProvider>();
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo($"/messages/new?to={Omar.Id}");
        var cut = Render<MessagesPage>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=messages-unavailable]")));
        Assert.DoesNotContain("Omar", cut.Markup);
        Assert.DoesNotContain("عمر", cut.Markup);
    }

    [Fact]
    public async Task Deep_link_to_an_allowed_person_opens_the_form_with_them_picked()
    {
        await SignInAsync(Sara);
        _dialogs = Render<MudDialogProvider>();
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo($"/messages/new?to={Huda.Id}");
        var cut = Render<MessagesPage>();

        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=compose]")));
        _dialogs.WaitForAssertion(() => Assert.Contains("aria-selected=\"true\"", _dialogs.Find($"[data-testid=picker-{Huda.Id}]").InnerHtml));
        Assert.NotNull(cut);
    }

    [Fact]
    public async Task Group_stops_at_the_limit_with_the_message()
    {
        await SignInAsync(Huda);
        MessagingApi.Related.Add((Huda.Id, Omar.Id));
        MessagingApi.Related.Add((Huda.Id, Khaled.Id));
        MessagingApi.Limits = MessagingApi.Limits with { MaxGroupSize = 3 }; // the caller plus two
        OpenNew();
        _dialogs.Find("[data-testid=compose-group]").Click();
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll($"[data-testid=picker-{Khaled.Id}]")));

        _dialogs.Find($"[data-testid=picker-{Sara.Id}]").Click();
        _dialogs.Find($"[data-testid=picker-{Omar.Id}]").Click();
        Assert.Empty(_dialogs.FindAll("[data-testid=picker-limit]"));
        _dialogs.Find($"[data-testid=picker-{Khaled.Id}]").Click();

        _dialogs.WaitForAssertion(() => Assert.Contains("3", _dialogs.Find("[data-testid=picker-limit]").TextContent));
        Assert.DoesNotContain("aria-selected=\"true\"", _dialogs.Find($"[data-testid=picker-{Khaled.Id}]").InnerHtml);
    }

    [Fact]
    public async Task Owner_can_set_the_group_read_only()
    {
        await SignInAsync(Huda);
        MessagingApi.Related.Add((Huda.Id, Omar.Id));
        MessagingApi.Related.Add((Huda.Id, Khaled.Id));
        var g = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Group, [Khaled.Id, Omar.Id], "Class 10-A", "Welcome"));
        var cut = RenderPage(g.Id);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=thread-members]")));

        cut.Find("[data-testid=thread-members]").Click();
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=members-readonly]")));
        _dialogs.Find("[data-testid=members-readonly]").Change(true);

        MessagingApi.Me = Khaled; // a guardian could post (D77); read-only now stops them
        _dialogs.WaitForAssertion(() => Assert.True(MessagingApi.GetConversationAsync(g.Id).Result.IsReadOnly));
        MessagingApi.Me = Huda;
        Assert.False((await MessagingApi.GetConversationAsync(g.Id)).IsReadOnly); // staff still post
    }

    [Fact]
    public async Task A_member_who_is_not_the_owner_only_reads_the_list()
    {
        await SignInAsync(Huda);
        MessagingApi.Related.Add((Huda.Id, Omar.Id));
        var g = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Group, [Sara.Id, Omar.Id], "Class 10-A", "Welcome"));
        await SignInAsync(Sara);
        var cut = RenderPage(g.Id);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=thread-members]")));

        cut.Find("[data-testid=thread-members]").Click();

        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=members-list]")));
        Assert.Empty(_dialogs.FindAll("[data-testid=members-readonly]"));
        Assert.Empty(_dialogs.FindAll("[data-testid=members-add]"));
        Assert.Empty(_dialogs.FindAll("[data-testid^=member-remove-]"));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new System.Globalization.CultureInfo("ar");
        await SignInAsync(Sara);
        OpenNew();

        Assert.True(Culture.IsRightToLeft);
        Assert.Contains("رسالة جديدة", _dialogs.Markup);
    }

    [Fact]
    public async Task Adding_members_never_offers_the_people_already_in_the_group()
    {
        await SignInAsync(Huda);
        MessagingApi.Related.Add((Huda.Id, Omar.Id));
        MessagingApi.Related.Add((Huda.Id, Khaled.Id));
        var g = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Group, [Sara.Id, Omar.Id], "Class 10-A", "Welcome"));
        var cut = RenderPage(g.Id);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=thread-members]")));

        cut.Find("[data-testid=thread-members]").Click();
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll($"[data-testid=picker-{Khaled.Id}]")));

        var offered = PickerIds(_dialogs);
        Assert.Contains($"picker-{Khaled.Id}", offered); // someone who is not in the group is offered
        Assert.DoesNotContain($"picker-{Sara.Id}", offered);
        Assert.DoesNotContain($"picker-{Omar.Id}", offered);
    }

    [Fact]
    public async Task A_teacher_who_reaches_students_gets_no_safeguarding_row_and_a_manager_does()
    {
        await SignInAsync(Huda);
        OpenNew();
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll($"[data-testid=picker-{Sara.Id}]"))); // the teacher reaches the student...
        Assert.Empty(_dialogs.FindAll("[data-testid=picker-d69]")); // ...so the "no students here" row would be wrong

        await DisposeComponentsAsync();
        await SignInAsync(FakeMessagingApi.Admin);
        OpenNew();
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=picker-d69]")));
    }

    [Fact]
    public async Task What_you_picked_stays_visible_as_chips_and_can_be_removed()
    {
        await SignInAsync(Sara);
        OpenNew();

        _dialogs.Find($"[data-testid=picker-{Huda.Id}]").Click();

        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll($"[data-testid=picker-chip-{Huda.Id}]")));
        Assert.False(string.IsNullOrWhiteSpace(_dialogs.Find($"[data-testid=picker-chip-remove-{Huda.Id}]").GetAttribute("aria-label")));
        _dialogs.Find("input[data-testid=picker-search]").Change("zzz"); // a new search does not hide the pick
        Assert.NotEmpty(_dialogs.FindAll($"[data-testid=picker-chip-{Huda.Id}]"));

        _dialogs.Find($"[data-testid=picker-chip-remove-{Huda.Id}]").Click();

        _dialogs.WaitForAssertion(() => Assert.Empty(_dialogs.FindAll($"[data-testid=picker-chip-{Huda.Id}]")));
    }

    [Fact]
    public async Task Role_captions_name_one_role_in_the_singular()
    {
        Culture.Current = new System.Globalization.CultureInfo("en");
        await SignInAsync(Sara);
        OpenNew();
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll($"[data-testid=picker-{Huda.Id}]")));

        var caption = _dialogs.Find($"[data-testid=picker-{Huda.Id}] .lms-picker-caption").TextContent;

        Assert.Equal("Teacher", caption);
        Assert.DoesNotContain("organisation", _dialogs.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Members_mark_the_owner_and_you()
    {
        await SignInAsync(Huda);
        MessagingApi.Related.Add((Huda.Id, Omar.Id));
        var g = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Group, [Sara.Id, Omar.Id], "10-A", "Welcome"));
        var cut = RenderPage(g.Id);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=thread-members]")));

        cut.Find("[data-testid=thread-members]").Click();

        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll($"[data-testid=member-owner-{Huda.Id}]")));
        Assert.NotEmpty(_dialogs.FindAll($"[data-testid=member-you-{Huda.Id}]"));
        Assert.Empty(_dialogs.FindAll($"[data-testid=member-owner-{Sara.Id}]"));
        Assert.Contains("lms-touch", _dialogs.Find($"[data-testid=member-remove-{Sara.Id}]").ClassName);
    }

    [Fact]
    public async Task The_compose_and_members_dialogs_go_full_screen_on_phones()
    {
        await SignInAsync(Sara);
        OpenNew();
        Assert.NotEmpty(_dialogs.FindAll(".lms-dialog-fs"));

        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Lms.Client"))) dir = Path.GetDirectoryName(dir);
        var css = File.ReadAllText(Path.Combine(dir!, "src", "Lms.Client", "wwwroot", "css", "app.css"));
        Assert.Contains("@media (max-width: 599px) { .lms-dialog-fs.mud-dialog", css);
    }

    [Fact]
    public void The_search_clear_button_is_labelled_in_both_languages()
    {
        var l = Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<Lms.Client.Resources.SharedResource>>();
        Assert.False(l["MudInput_Clear"].ResourceNotFound);
    }

    [Fact]
    public async Task After_adding_members_the_picker_is_clean_and_no_longer_offers_them()
    {
        await SignInAsync(Huda);
        MessagingApi.Related.Add((Huda.Id, Omar.Id));
        MessagingApi.Related.Add((Huda.Id, Khaled.Id));
        var g = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Group, [Sara.Id, Omar.Id], "Class 10-A", "Welcome"));
        var cut = RenderPage(g.Id);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=thread-members]")));
        cut.Find("[data-testid=thread-members]").Click();
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll($"[data-testid=picker-{Khaled.Id}]")));
        _dialogs.Find($"[data-testid=picker-{Khaled.Id}]").Click();

        _dialogs.Find("[data-testid=members-add]").Click();

        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll($"[data-testid=member-{Khaled.Id}]")));
        Assert.Empty(_dialogs.FindAll("[data-testid=picker-to]")); // the chips of the people just added are gone
        Assert.DoesNotContain($"picker-{Khaled.Id}", PickerIds(_dialogs)); // and they are not offered again
        Assert.True(_dialogs.Find("[data-testid=members-add]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Opening_the_new_message_link_again_reopens_the_form()
    {
        await SignInAsync(Sara);
        _dialogs = Render<MudDialogProvider>();
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo($"/messages/new?to={Huda.Id}");
        Render<MessagesPage>();
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=compose]")));
        _dialogs.Find("[data-testid=compose-cancel]").Click();
        _dialogs.WaitForAssertion(() => Assert.Empty(_dialogs.FindAll("[data-testid=compose]")));

        navigation.NavigateTo($"/messages/new?to={Huda.Id}"); // the page instance is reused

        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=compose]")));
    }
}
