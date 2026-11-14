using System.Globalization;
using Bunit;
using Lms.Client.Components.Messaging;
using Lms.Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Lms.Client.Tests.Components.Messaging;

/// <summary>The fourth set of M6 parts (spec #615 §3, §7a): the recipient picker, New message, and the messaging rules matrix with the D69 floor.</summary>
public sealed class MessagingComponents4Tests : ClientTestContext
{
    private static void English() => FakeCultureService.Apply(new CultureInfo("en"));

    private static Task<IEnumerable<RecipientChoice>> NoSearch(string text, CancellationToken token) => Task.FromResult(Enumerable.Empty<RecipientChoice>());

    [Fact]
    public async Task Picker_adds_a_person_once_and_never_a_label_a_near_miss_or_the_safeguarding_row()
    {
        English();
        var picked = new List<RecipientChoice>();
        var cut = Render<RecipientPicker>(p => p.Add(c => c.Value, picked).Add(c => c.SearchFunc, NoSearch));

        var huda = new RecipientChoice("u1", RecipientKind.Person, "Huda Al-Harbi", "Guardian of Omar Saleh · 10-A");
        await cut.InvokeAsync(() => cut.Instance.PickAsync(huda));
        await cut.InvokeAsync(() => cut.Instance.PickAsync(huda));
        await cut.InvokeAsync(() => cut.Instance.PickAsync(new RecipientChoice("l", RecipientKind.GroupLabel, "People")));
        await cut.InvokeAsync(() => cut.Instance.PickAsync(new RecipientChoice("g2", RecipientKind.Group, "Physics 1 · 10-B — guardians", DisabledReason: "Not allowed: you don't teach 10-B")));
        await cut.InvokeAsync(() => cut.Instance.PickAsync(new RecipientChoice("d69", RecipientKind.Safeguarding, "No students here")));

        Assert.Single(picked);
        Assert.Equal("Huda Al-Harbi", cut.Find("[data-testid=to-u1] bdi").TextContent);
    }

    [Fact]
    public void Only_groups_and_people_without_a_reason_are_selectable()
    {
        Assert.True(new RecipientChoice("p", RecipientKind.Person, "A").Selectable);
        Assert.True(new RecipientChoice("g", RecipientKind.Group, "G").Selectable);
        Assert.False(new RecipientChoice("g", RecipientKind.Group, "G", DisabledReason: "x").Selectable);
        Assert.False(new RecipientChoice("s", RecipientKind.Safeguarding, "S").Selectable);
        Assert.False(new RecipientChoice("l", RecipientKind.GroupLabel, "L").Selectable);
    }

    [Fact]
    public async Task Sending_needs_a_recipient_and_a_message()
    {
        English();
        var sent = 0;
        var draft = new MessageDraft();
        var cut = Render<ComposeMessageForm>(p => p.Add(c => c.Draft, draft).Add(c => c.SearchFunc, NoSearch)
            .Add(c => c.OnSend, EventCallback.Factory.Create<MessageDraft>(this, _ => sent++)));

        await cut.Find("[data-testid=compose-send]").ClickAsync(new MouseEventArgs());
        Assert.Equal(0, sent);
        Assert.Contains("Choose at least one recipient.", cut.Markup);
        Assert.Contains("Write a message.", cut.Markup);

        draft.To.Add(new RecipientChoice("u1", RecipientKind.Person, "Huda Al-Harbi"));
        draft.Body = "Hello";
        await cut.Find("[data-testid=compose-send]").ClickAsync(new MouseEventArgs());
        Assert.Equal(1, sent);
    }

    [Fact]
    public void A_group_message_says_replies_go_to_everyone()
    {
        English();
        var draft = new MessageDraft();
        draft.To.Add(new RecipientChoice("g1", RecipientKind.Group, "Physics 1 · 10-A — guardians", SizeText: "24 people"));
        var cut = Render<ComposeMessageForm>(p => p.Add(c => c.Draft, draft).Add(c => c.SearchFunc, NoSearch));

        Assert.Contains("Replies go to everyone in the group.", cut.Find("[data-testid=compose-group-note]").TextContent);
        Assert.True(draft.IsGroup);
    }

    [Fact]
    public void D69_floor_locks_students_and_guardians_and_gives_managers_groups_only()
    {
        var cells = MessagingRulesMatrix.D69Defaults();

        Assert.True(cells[(MessagingRole.Student, MessagingRole.Student)].Locked);
        Assert.Equal(MessagingVia.NotAllowed, cells[(MessagingRole.Student, MessagingRole.Student)].Via);
        Assert.Equal(MessagingVia.Relationship, cells[(MessagingRole.Student, MessagingRole.Teacher)].Via);
        Assert.False(cells[(MessagingRole.Student, MessagingRole.Teacher)].Locked);
        Assert.True(cells[(MessagingRole.Guardian, MessagingRole.Manager)].Locked);
        Assert.Equal(MessagingVia.GroupsOnly, cells[(MessagingRole.Manager, MessagingRole.Student)].Via);
        Assert.Equal(MessagingVia.GroupsOnly, cells[(MessagingRole.OrgAdmin, MessagingRole.Student)].Via);
        Assert.True(cells[(MessagingRole.OrgAdmin, MessagingRole.Student)].Locked);
    }

    [Fact]
    public void Each_default_is_the_ceiling_and_only_narrower_values_are_allowed()
    {
        var cells = MessagingRulesMatrix.D69Defaults();
        var studentToTeacher = cells[(MessagingRole.Student, MessagingRole.Teacher)];

        Assert.Equal(MessagingVia.Relationship, studentToTeacher.Max);
        Assert.False(studentToTeacher.Allows(MessagingVia.Anywhere));
        Assert.False(studentToTeacher.Allows(MessagingVia.SameUnit));
        Assert.True(studentToTeacher.Allows(MessagingVia.Relationship));
        Assert.True(studentToTeacher.Allows(MessagingVia.NotAllowed));
        Assert.False(cells[(MessagingRole.Manager, MessagingRole.Teacher)].Allows(MessagingVia.Anywhere)); // Same unit is the ceiling
        Assert.True(cells[(MessagingRole.OrgAdmin, MessagingRole.Teacher)].Allows(MessagingVia.Anywhere));
        Assert.False(cells[(MessagingRole.Student, MessagingRole.Student)].Allows(MessagingVia.NotAllowed)); // locked: nothing changes
    }

    [Fact]
    public async Task Set_rejects_a_wider_value_and_any_change_to_a_locked_cell_and_raises_nothing()
    {
        English();
        var cells = MessagingRulesMatrix.D69Defaults();
        var changed = 0;
        var cut = Render<MessagingRulesMatrix>(p => p.Add(c => c.Cells, cells).Add(c => c.OnChanged, EventCallback.Factory.Create(this, () => changed++)));

        var guardianToCounselor = cells[(MessagingRole.Guardian, MessagingRole.Counselor)];
        Assert.False(await cut.InvokeAsync(() => cut.Instance.SetAsync(guardianToCounselor, MessagingVia.Anywhere)));
        Assert.Equal(MessagingVia.Relationship, guardianToCounselor.Via);

        var locked = cells[(MessagingRole.Manager, MessagingRole.Student)];
        Assert.False(await cut.InvokeAsync(() => cut.Instance.SetAsync(locked, MessagingVia.NotAllowed)));
        Assert.Equal(MessagingVia.GroupsOnly, locked.Via);
        Assert.Equal(0, changed);

        Assert.True(await cut.InvokeAsync(() => cut.Instance.SetAsync(guardianToCounselor, MessagingVia.NotAllowed)));
        Assert.Equal(MessagingVia.NotAllowed, guardianToCounselor.Via);
        Assert.Equal(1, changed);
        Assert.True(guardianToCounselor.Allows(MessagingVia.Relationship)); // narrowed, and can return up to its default
    }

    [Fact]
    public async Task Wider_options_are_shown_disabled_with_the_reason()
    {
        English();
        var popovers = Render<MudBlazor.MudPopoverProvider>();
        var cut = Render<MessagingRulesMatrix>(p => p.Add(c => c.Cells, MessagingRulesMatrix.D69Defaults()));

        await cut.Find("[data-testid=rules-Student-Teacher] .mud-select .mud-input-control").MouseDownAsync(new MouseEventArgs());
        var items = popovers.FindAll(".mud-popover-open .mud-list-item");
        var anywhere = items.Single(i => i.TextContent.Contains("Anyone"));
        Assert.Contains("mud-list-item-disabled", anywhere.ClassName);
        Assert.Contains("wider than the safeguarding default", anywhere.TextContent);
        Assert.DoesNotContain("mud-list-item-disabled", items.Single(i => i.TextContent.Trim().StartsWith("Not allowed")).ClassName);
    }

    [Fact]
    public void Matrix_shows_a_select_only_for_unlocked_cells_and_labels_rows_for_phones()
    {
        English();
        var cut = Render<MessagingRulesMatrix>(p => p.Add(c => c.Cells, MessagingRulesMatrix.D69Defaults()));

        Assert.Empty(cut.Find("[data-testid=rules-Student-Student]").QuerySelectorAll(".mud-select"));
        Assert.Contains("Set by the platform", cut.Find("[data-testid=rules-Manager-Student]").TextContent);
        Assert.NotEmpty(cut.Find("[data-testid=rules-Teacher-Guardian]").QuerySelectorAll(".mud-select"));
        Assert.Equal("Guardian", cut.Find("[data-testid=rules-Teacher-Guardian]").GetAttribute("data-label"));
        Assert.Equal(7, cut.FindAll("tbody tr").Count);
    }
}
