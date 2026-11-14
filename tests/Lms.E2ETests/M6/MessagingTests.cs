using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M6;

/// <summary>
/// M6 exit demo 2 (plan Task 34, QA2): student, guardian and teacher conversations under the D69 safeguarding rules, realtime at 360 px. Skipped until the M6
/// backend (messaging kernel #733, recipients and rules, the hub) and the FE1 pages (#753, #757, #761, #763) are on main, and the M6 demo seed has created
/// the people of <see cref="MessagingWorld"/>. Each scenario runs in Arabic (asserting <c>dir="rtl"</c>) and English. Take <c>e2e.lock</c> before any run; one stack
/// at a time. Ids: FE1's pages (<see cref="MessagesPage"/>, <see cref="NewConversationDialog"/>) and FE2's rules page (<see cref="MessagingRulesPage"/>, #756).
/// The oracle for who may reach whom is the D69 grid in <c>M6SafeguardingMatrixTests</c> (#770); this suite only shows it through the screens.
/// No SMS anywhere (D76): no scenario looks for an SMS option and none may find one.
/// </summary>
public sealed class MessagingTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    private const string Backend = "M6 messaging backend (#733 kernel, recipients and rules, hub) not on main";
    private const string Hub = "M6 realtime hub (Task 12) and FE1 realtime (#763) not on main";
    private const string Groups = "M6 group routes (#733, #757) not on main";
    private const string Moderation = "Student-side blocking and the rules save need the M6 moderation and rules routes (#760, #754)";
    private const string Phone = "M6 messaging backend and FE1 realtime (#763) not on main; needs a touch context";

    // ---- Student and teacher: the allowed path, in realtime ----

    [Theory(Skip = Hub)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Student_messages_own_teacher_and_teacher_replies_in_realtime(string culture)
    {
        await using var student = await UserSession.OpenAsync(Fixture, MessagingWorld.Student1, culture, test: nameof(Student_messages_own_teacher_and_teacher_replies_in_realtime));
        await using var teacher = await UserSession.OpenAsync(Fixture, MessagingWorld.Teacher, culture, test: nameof(Student_messages_own_teacher_and_teacher_replies_in_realtime));
        var teacherId = await teacher.UserIdAsync();

        await student.Messages.OpenInboxAsync();
        await student.NewConversation.OpenAsync(student.Messages);
        await student.NewConversation.StartDirectAsync(teacherId, "Good morning, a question about homework");
        var conversation = student.Messages.CurrentConversationId();

        // The teacher's inbox gets the row without a reload, unread, and the thread shows the message.
        await teacher.Messages.OpenInboxAsync();
        await Expect(teacher.Messages.Row(conversation)).ToBeVisibleAsync();
        await teacher.Messages.OpenFromInboxAsync(conversation);
        await Expect(teacher.Messages.Thread).ToContainTextAsync("a question about homework");

        // The teacher answers; the student's open thread shows it with no reload and no duplicate.
        await student.Messages.OpenAsync(conversation);
        await teacher.Messages.SendAsync("Page 12, exercises 1 to 3");
        await Expect(student.Messages.Thread).ToContainTextAsync("Page 12, exercises 1 to 3");
        Assert.Single(await student.Messages.BubbleTextsAsync(), t => t.Contains("Page 12"));
        await Expect(student.Messages.Announcer).ToContainTextAsync("Page 12");

        await student.AssertDirectionAsync(culture);
        await teacher.AssertDirectionAsync(culture);
    }

    // ---- D69: who can be found ----

    [Theory(Skip = Backend)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Student_cannot_find_another_student_in_the_picker(string culture)
    {
        await using var student = await UserSession.OpenAsync(Fixture, MessagingWorld.Student1, culture, test: nameof(Student_cannot_find_another_student_in_the_picker));
        await using var classmate = await UserSession.OpenAsync(Fixture, MessagingWorld.Student2, culture, test: nameof(Student_cannot_find_another_student_in_the_picker));
        var classmateId = await classmate.UserIdAsync();

        await student.Messages.OpenInboxAsync();
        await student.NewConversation.OpenAsync(student.Messages);
        await Expect(student.NewConversation.Option(classmateId)).ToHaveCountAsync(0);

        // Searching for the classmate by name finds nothing and says nothing about why (no rule is named, no name is echoed).
        var name = await classmate.Page.GetByTestId("user-menu").InnerTextAsync();
        await student.NewConversation.SearchAsync(name.Trim());
        await Expect(student.NewConversation.Option(classmateId)).ToHaveCountAsync(0);
        await Expect(student.NewConversation.None).ToBeVisibleAsync();
        Assert.DoesNotContain(name.Trim(), await student.NewConversation.None.InnerTextAsync());

        // The "?to=" deep link to the classmate gives the same neutral answer, never the form.
        await student.Page.GotoAsync($"/messages/new?to={classmateId}");
        await Expect(student.Messages.Unavailable).ToBeVisibleAsync();
        await Expect(student.NewConversation.Dialog).ToHaveCountAsync(0);

        // No group option for a student (L5).
        await student.Messages.OpenInboxAsync();
        await student.NewConversation.OpenAsync(student.Messages);
        await Expect(student.NewConversation.GroupKind).ToHaveCountAsync(0);
        await student.AssertDirectionAsync(culture);
    }

    [Theory(Skip = Backend)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Guardian_messages_the_childs_teacher_and_cannot_reach_another_guardian(string culture)
    {
        await using var guardian = await UserSession.OpenAsync(Fixture, MessagingWorld.Guardian1, culture, test: nameof(Guardian_messages_the_childs_teacher_and_cannot_reach_another_guardian));
        await using var other = await UserSession.OpenAsync(Fixture, MessagingWorld.Guardian2, culture, test: nameof(Guardian_messages_the_childs_teacher_and_cannot_reach_another_guardian));
        await using var teacher = await UserSession.OpenAsync(Fixture, MessagingWorld.Teacher, culture, test: nameof(Guardian_messages_the_childs_teacher_and_cannot_reach_another_guardian));
        var teacherId = await teacher.UserIdAsync();
        var otherGuardianId = await other.UserIdAsync();

        await guardian.Messages.OpenInboxAsync();
        await guardian.NewConversation.OpenAsync(guardian.Messages);
        await Expect(guardian.NewConversation.Option(teacherId)).ToBeVisibleAsync();
        await Expect(guardian.NewConversation.Option(otherGuardianId)).ToHaveCountAsync(0);
        await guardian.NewConversation.StartDirectAsync(teacherId, "Is my child keeping up in class?");

        await Expect(guardian.Messages.Thread).ToContainTextAsync("keeping up");
        await guardian.AssertDirectionAsync(culture);
    }

    [Theory(Skip = Backend)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Manager_cannot_open_a_private_conversation_with_a_student(string culture)
    {
        await using var manager = await UserSession.OpenAsync(Fixture, MessagingWorld.Manager, culture, test: nameof(Manager_cannot_open_a_private_conversation_with_a_student));
        await using var student = await UserSession.OpenAsync(Fixture, MessagingWorld.Student1, culture, test: nameof(Manager_cannot_open_a_private_conversation_with_a_student));
        var studentId = await student.UserIdAsync();

        await manager.Messages.OpenInboxAsync();
        await manager.NewConversation.OpenAsync(manager.Messages);
        await Expect(manager.NewConversation.Option(studentId)).ToHaveCountAsync(0);
        await Expect(manager.NewConversation.SafeguardingRow).ToBeVisibleAsync(); // the one D69 row, in place of any student
        await manager.Page.GotoAsync($"/messages/new?to={studentId}");
        await Expect(manager.Messages.Unavailable).ToBeVisibleAsync();

        // The API says the same: the forced start is refused with messaging_not_allowed and no reason that names the rule.
        var forced = await manager.Api.SendAsync("POST", "/api/conversations",
            new { kind = "Direct", participantUserIds = new[] { studentId }, title = (string?)null, firstMessage = "hello" });
        Assert.Equal(403, forced.Status);
        Assert.Contains("messaging_not_allowed", await forced.TextAsync());
        await manager.AssertDirectionAsync(culture);
    }

    // ---- D74 and D77: groups ----

    [Theory(Skip = Groups)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Teacher_creates_a_class_course_group_and_students_read_but_cannot_post(string culture)
    {
        await using var teacher = await UserSession.OpenAsync(Fixture, MessagingWorld.Teacher, culture, test: nameof(Teacher_creates_a_class_course_group_and_students_read_but_cannot_post));
        await using var student = await UserSession.OpenAsync(Fixture, MessagingWorld.Student1, culture, test: nameof(Teacher_creates_a_class_course_group_and_students_read_but_cannot_post));
        var studentId = await student.UserIdAsync();
        var student2Id = await UserIdOfAsync(culture, MessagingWorld.Student2);

        await teacher.Messages.OpenInboxAsync();
        await teacher.NewConversation.OpenAsync(teacher.Messages);
        await teacher.NewConversation.StartGroupAsync("10-A Maths", [studentId, student2Id], "Welcome to the class group");
        var group = teacher.Messages.CurrentConversationId();

        await student.Messages.OpenInboxAsync();
        await student.Messages.OpenFromInboxAsync(group);
        await Expect(student.Messages.Thread).ToContainTextAsync("Welcome to the class group");
        Assert.True(await student.Messages.HasNoComposerAsync()); // D74: students read, they never post in a group
        Assert.Equal(0, await student.Messages.ComposerSend.CountAsync());

        // The server says it too (D74): CanPost is false with ReadOnlyReason group_students_read_only, and a forced post is 403 conversation_read_only.
        await AssertReadOnlyAsync(student, group, "group_students_read_only", "forced from the console");
        await student.AssertDirectionAsync(culture);
    }

    [Theory(Skip = Groups)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Student_replies_privately_to_the_teacher_from_the_group(string culture)
    {
        await using var teacher = await UserSession.OpenAsync(Fixture, MessagingWorld.Teacher, culture, test: nameof(Student_replies_privately_to_the_teacher_from_the_group));
        await using var student = await UserSession.OpenAsync(Fixture, MessagingWorld.Student1, culture, test: nameof(Student_replies_privately_to_the_teacher_from_the_group));
        var group = await TeacherGroupWithStudentAsync(teacher, student);

        await student.Messages.OpenAsync(group);
        await Expect(student.Messages.ReplyPrivately).ToBeVisibleAsync();
        await student.Messages.ReplyPrivately.ClickAsync();
        await Expect(student.NewConversation.Dialog).ToBeVisibleAsync();
        await student.NewConversation.ComposerText.FillAsync("Can I have the notes for last week?");
        await student.NewConversation.Send.ClickAsync();

        // A private conversation of the two, not a post in the group.
        await Expect(student.Messages.Thread).ToContainTextAsync("notes for last week");
        Assert.NotEqual(group, student.Messages.CurrentConversationId());
        await student.AssertDirectionAsync(culture);
    }

    [Theory(Skip = Groups)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Students_in_a_staff_group_are_read_only(string culture)
    {
        await using var manager = await UserSession.OpenAsync(Fixture, MessagingWorld.Manager, culture, test: nameof(Students_in_a_staff_group_are_read_only));
        await using var student = await UserSession.OpenAsync(Fixture, MessagingWorld.Student1, culture, test: nameof(Students_in_a_staff_group_are_read_only));
        var group = await TeacherGroupWithStudentAsync(manager, student); // a manager's group (the owner is staff)

        await student.Messages.OpenAsync(group);
        Assert.True(await student.Messages.HasNoComposerAsync());
        await AssertReadOnlyAsync(student, group, "group_students_read_only", "forced");
    }

    [Theory(Skip = Groups)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Guardian_cannot_post_in_a_manager_run_group(string culture)
    {
        await using var manager = await UserSession.OpenAsync(Fixture, MessagingWorld.Manager, culture, test: nameof(Guardian_cannot_post_in_a_manager_run_group));
        await using var guardian = await UserSession.OpenAsync(Fixture, MessagingWorld.Guardian1, culture, test: nameof(Guardian_cannot_post_in_a_manager_run_group));
        var group = await TeacherGroupAsync(manager, [await guardian.UserIdAsync(), await UserIdOfAsync(culture, MessagingWorld.Teacher)]);

        await guardian.Messages.OpenAsync(group);
        Assert.True(await guardian.Messages.HasNoComposerAsync()); // D77: guardians post only in teacher-run groups
        await AssertReadOnlyAsync(guardian, group, "group_guardians_read_only", "forced");
    }

    [Theory(Skip = Groups)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Guardian_posts_in_a_teacher_run_group_and_other_guardians_read_it(string culture)
    {
        await using var teacher = await UserSession.OpenAsync(Fixture, MessagingWorld.Teacher, culture, test: nameof(Guardian_posts_in_a_teacher_run_group_and_other_guardians_read_it));
        await using var guardian = await UserSession.OpenAsync(Fixture, MessagingWorld.Guardian1, culture, test: nameof(Guardian_posts_in_a_teacher_run_group_and_other_guardians_read_it));
        await using var other = await UserSession.OpenAsync(Fixture, MessagingWorld.Guardian2, culture, test: nameof(Guardian_posts_in_a_teacher_run_group_and_other_guardians_read_it));
        var group = await TeacherGroupAsync(teacher, [await guardian.UserIdAsync(), await other.UserIdAsync()]);

        await guardian.Messages.OpenAsync(group);
        await Expect(guardian.Messages.ComposerText).ToBeVisibleAsync(); // D77: a guardian may post in a teacher-run group
        await guardian.Messages.SendAsync("Thank you, we will be there");

        await other.Messages.OpenAsync(group);
        await Expect(other.Messages.Thread).ToContainTextAsync("we will be there"); // read by the other guardian: the group is a shared space
        await guardian.AssertDirectionAsync(culture);
    }

    // ---- L25: the reply path ----

    [Theory(Skip = Backend)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Guardian_replies_to_a_manager_who_wrote_first_but_cannot_start_one(string culture)
    {
        await using var manager = await UserSession.OpenAsync(Fixture, MessagingWorld.Manager, culture, test: nameof(Guardian_replies_to_a_manager_who_wrote_first_but_cannot_start_one));
        await using var guardian = await UserSession.OpenAsync(Fixture, MessagingWorld.Guardian1, culture, test: nameof(Guardian_replies_to_a_manager_who_wrote_first_but_cannot_start_one));
        var managerId = await manager.UserIdAsync();

        // The guardian cannot start one: the manager is not in the picker.
        await guardian.Messages.OpenInboxAsync();
        await guardian.NewConversation.OpenAsync(guardian.Messages);
        await Expect(guardian.NewConversation.Option(managerId)).ToHaveCountAsync(0);
        await guardian.NewConversation.Cancel.ClickAsync();

        // The manager writes first (staff to guardian is allowed); the guardian can answer in that conversation.
        await manager.Messages.OpenInboxAsync();
        await manager.NewConversation.OpenAsync(manager.Messages);
        await manager.NewConversation.StartDirectAsync(await guardian.UserIdAsync(), "A note about the school trip");
        var conversation = manager.Messages.CurrentConversationId();

        await guardian.Messages.OpenAsync(conversation);
        await Expect(guardian.Messages.ComposerText).ToBeVisibleAsync();
        await guardian.Messages.SendAsync("Thank you, noted");
        await Expect(manager.Messages.Thread).ToContainTextAsync("noted");
    }

    // ---- Rules: tighten, never loosen a floor cell ----

    [Theory(Skip = Moderation)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Admin_tightens_a_rule_and_cannot_loosen_a_floor_cell(string culture)
    {
        await using var admin = await UserSession.OpenAsync(Fixture, MessagingWorld.OrgAdmin, culture, test: nameof(Admin_tightens_a_rule_and_cannot_loosen_a_floor_cell));
        var rules = new MessagingRulesPage(admin.Page);
        await rules.OpenAsync();

        // Tightening Teacher to Student is accepted and kept after a reload.
        var cell = rules.Cell("Teacher", "Student");
        await cell.SetCheckedAsync(false);
        await rules.SaveAsync();
        await admin.Page.ReloadAsync();
        await rules.Title.WaitForAsync();
        Assert.False(await cell.IsCheckedAsync());

        // A floor cell (Student to Student) cannot be loosened: the control is off and a forced save is refused, with the cell named.
        var floor = rules.Cell("Student", "Student");
        Assert.True(await floor.IsDisabledAsync());
        var forced = await admin.Api.SendAsync("PUT", "/api/comms/messaging-rules",
            new { cells = new[] { new { fromRole = "Student", toRole = "Student", mode = "Allow" } } });
        Assert.Equal(400, forced.Status);
        Assert.Contains("rules_looser_than_default", await forced.TextAsync());

        // Put it back so the next run starts from the defaults.
        await cell.SetCheckedAsync(true);
        await rules.SaveAsync();
        await admin.AssertDirectionAsync(culture);
    }

    // ---- Deleting ----

    [Theory(Skip = Backend)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Sender_deletes_within_15_minutes_and_everyone_sees_message_deleted(string culture)
    {
        await using var teacher = await UserSession.OpenAsync(Fixture, MessagingWorld.Teacher, culture, test: nameof(Sender_deletes_within_15_minutes_and_everyone_sees_message_deleted));
        await using var student = await UserSession.OpenAsync(Fixture, MessagingWorld.Student1, culture, test: nameof(Sender_deletes_within_15_minutes_and_everyone_sees_message_deleted));
        var conversation = await DirectAsync(teacher, student);

        await teacher.Messages.OpenAsync(conversation);
        await teacher.Messages.SendAsync("Wrong room, ignore this");
        var bubbles = teacher.Messages.Bubbles;
        var last = bubbles.Last;
        var id = Guid.Parse((await last.GetAttributeAsync("data-testid"))!["msg-".Length..]);
        await teacher.Messages.DeleteButton(id).ClickAsync();
        await Expect(teacher.Messages.Message(id)).Not.ToContainTextAsync("Wrong room");

        await student.Messages.OpenAsync(conversation);
        await Expect(student.Messages.Message(id)).Not.ToContainTextAsync("Wrong room");
        await Expect(student.Messages.Message(id)).ToContainTextAsync(culture == "ar" ? "حُذفت" : "deleted");
        // The student never gets a delete button on the teacher's message (the server says who can delete).
        await Expect(student.Messages.DeleteButton(id)).ToHaveCountAsync(0);
        // A window of 15 minutes is the contract (D66); the closed-window answer (409 message_delete_window_closed) needs the dev clock and is a follow-up.
        Assert.Equal(15, MessagingWorld.DeleteWindowMinutes);
    }

    // ---- Phone, reconnect, keyboard ----

    [Theory(Skip = Phone)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Thread_works_at_360px_with_the_composer_above_the_keyboard(string culture)
    {
        await using var student = await UserSession.OpenAsync(Fixture, MessagingWorld.Student1, culture, width: 360, height: 640, touch: true,
            test: nameof(Thread_works_at_360px_with_the_composer_above_the_keyboard));
        await using var teacher = await UserSession.OpenAsync(Fixture, MessagingWorld.Teacher, culture, test: nameof(Thread_works_at_360px_with_the_composer_above_the_keyboard));
        var conversation = await DirectAsync(teacher, student);

        // The inbox is the list; opening a row replaces it with the thread, and Back returns to the list.
        await student.Messages.OpenInboxAsync();
        await student.Messages.OpenFromInboxAsync(conversation);
        await Expect(student.Messages.Back).ToBeVisibleAsync();
        await AssertNoSidewaysScrollAsync(student);

        // The composer is inside the viewport, under the messages, with a Send target of at least 44 px.
        var viewport = student.Page.ViewportSize!.Height;
        var composer = await student.Messages.Composer.BoundingBoxAsync();
        Assert.True(composer is not null && composer.Y + composer.Height <= viewport + 1, "the composer is below the fold");
        var send = await student.Messages.ComposerSend.BoundingBoxAsync();
        Assert.True(send is not null && send.Width >= 44 && send.Height >= 44, "Send is smaller than 44 px");

        // A new message from the teacher lands in the open thread without moving the composer; at the bottom it scrolls into view.
        await teacher.Messages.OpenAsync(conversation);
        await teacher.Messages.SendAsync("See you at nine");
        await Expect(student.Messages.Thread).ToContainTextAsync("See you at nine");
        await AssertNoSidewaysScrollAsync(student);

        await student.Messages.Back.ClickAsync();
        await Expect(student.Messages.InboxList).ToBeVisibleAsync();
        await student.AssertDirectionAsync(culture);
    }

    [Theory(Skip = Hub)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Reconnect_catches_up_without_duplicates(string culture)
    {
        await using var student = await UserSession.OpenAsync(Fixture, MessagingWorld.Student1, culture, width: 360, height: 640, test: nameof(Reconnect_catches_up_without_duplicates));
        await using var teacher = await UserSession.OpenAsync(Fixture, MessagingWorld.Teacher, culture, test: nameof(Reconnect_catches_up_without_duplicates));
        var conversation = await DirectAsync(teacher, student);

        await student.Messages.OpenAsync(conversation);
        await student.GoOfflineAsync();
        await Expect(student.Messages.Offline).ToBeVisibleAsync();

        await teacher.Messages.OpenAsync(conversation);
        await teacher.Messages.SendAsync("sent while offline 1");
        await teacher.Messages.SendAsync("sent while offline 2");

        await student.GoOnlineAsync();
        await Expect(student.Messages.Offline).ToBeHiddenAsync();
        await Expect(student.Messages.Thread).ToContainTextAsync("sent while offline 2");
        var texts = await student.Messages.BubbleTextsAsync();
        Assert.Single(texts, t => t.Contains("sent while offline 1"));
        Assert.Single(texts, t => t.Contains("sent while offline 2"));
    }

    [Theory(Skip = Backend)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Messaging_is_operable_by_keyboard_only(string culture)
    {
        await using var student = await UserSession.OpenAsync(Fixture, MessagingWorld.Student1, culture, test: nameof(Messaging_is_operable_by_keyboard_only));
        await using var teacher = await UserSession.OpenAsync(Fixture, MessagingWorld.Teacher, culture, test: nameof(Messaging_is_operable_by_keyboard_only));
        var conversation = await DirectAsync(teacher, student);
        var teacherId = await teacher.UserIdAsync();

        // Inbox: the rows are links with a name that says who, the unread count and the preview; Enter opens one.
        await student.Messages.OpenInboxAsync();
        var row = student.Messages.Row(conversation);
        Assert.False(string.IsNullOrWhiteSpace(await row.GetAttributeAsync("aria-label")));
        await row.FocusAsync();
        await student.Page.Keyboard.PressAsync("Enter");
        await student.Messages.Thread.WaitForAsync();

        // Thread: a labelled log region, one polite status, and Send reachable by Tab; Ctrl+Enter sends, Enter breaks the line.
        Assert.Equal("log", await student.Messages.ThreadBody.GetAttributeAsync("role"));
        Assert.False(string.IsNullOrWhiteSpace(await student.Messages.Thread.GetAttributeAsync("aria-label")));
        await student.Messages.ComposerText.FocusAsync();
        await student.Page.Keyboard.TypeAsync("line one");
        await student.Page.Keyboard.PressAsync("Enter");
        await student.Page.Keyboard.TypeAsync("line two");
        await student.Page.Keyboard.PressAsync("Control+Enter");
        await Expect(student.Messages.Thread).ToContainTextAsync("line two");

        // Picker: a labelled listbox; arrow keys and Enter pick a person; Escape closes the dialog and focus returns to "New message".
        await student.Messages.OpenInboxAsync();
        await student.Messages.NewMessageButton.FocusAsync();
        await student.Page.Keyboard.PressAsync("Enter");
        await student.NewConversation.Dialog.WaitForAsync();
        Assert.Equal("listbox", await student.NewConversation.List.GetAttributeAsync("role"));
        Assert.False(string.IsNullOrWhiteSpace(await student.NewConversation.List.GetAttributeAsync("aria-label")));
        await student.NewConversation.Option(teacherId).FocusAsync();
        await student.Page.Keyboard.PressAsync("Enter");
        await student.Page.Keyboard.PressAsync("Escape");
        await Expect(student.NewConversation.Dialog).ToBeHiddenAsync();
        await Expect(student.Messages.NewMessageButton).ToBeFocusedAsync();
        await student.AssertDirectionAsync(culture);
    }

    // ---- helpers (their bodies create the data through the real screens and API; written with the first live scenario) ----

    /// <summary>The server is the authority on who may post: the conversation says CanPost=false with the reason, and a forced post is 403 conversation_read_only (L25, D74, D77).</summary>
    private static async Task AssertReadOnlyAsync(UserSession session, Guid conversation, string reason, string forcedText)
    {
        var dto = await session.Api.JsonAsync(await session.Api.SendAsync("GET", $"/api/conversations/{conversation}"));
        Assert.False(dto.GetProperty("canPost").GetBoolean());
        Assert.Equal(reason, dto.GetProperty("readOnlyReason").GetString());
        var (status, problem) = await session.ForcePostAsync(conversation, forcedText);
        Assert.Equal(403, status);
        Assert.Contains("conversation_read_only", problem?.ToString());
    }

    /// <summary>No sideways scroll at 360 px (the page is never wider than the viewport).</summary>
    private static async Task AssertNoSidewaysScrollAsync(UserSession session)
    {
        var overflow = await session.Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.False(overflow, "the page scrolls sideways at 360 px");
    }

    /// <summary>A direct conversation of two people, made through the picker as the first one, with an opening message.</summary>
    private static async Task<Guid> DirectAsync(UserSession from, UserSession to)
    {
        await from.Messages.OpenInboxAsync();
        await from.NewConversation.OpenAsync(from.Messages);
        await from.NewConversation.StartDirectAsync(await to.UserIdAsync(), "Hello");
        return from.Messages.CurrentConversationId();
    }

    /// <summary>A group of the owner with one student (plus the other student of the class course), made through the picker.</summary>
    private async Task<Guid> TeacherGroupWithStudentAsync(UserSession owner, UserSession student) =>
        await TeacherGroupAsync(owner, [await student.UserIdAsync(), await UserIdOfAsync("en", MessagingWorld.Student2)]);

    private static async Task<Guid> TeacherGroupAsync(UserSession owner, IEnumerable<Guid> members)
    {
        await owner.Messages.OpenInboxAsync();
        await owner.NewConversation.OpenAsync(owner.Messages);
        await owner.NewConversation.StartGroupAsync("QA group " + Guid.NewGuid().ToString("N")[..6], members, "Welcome");
        return owner.Messages.CurrentConversationId();
    }

    private async Task<Guid> UserIdOfAsync(string culture, string seedUser)
    {
        await using var session = await UserSession.OpenAsync(Fixture, seedUser, culture, test: "userid");
        return await session.UserIdAsync();
    }
}
