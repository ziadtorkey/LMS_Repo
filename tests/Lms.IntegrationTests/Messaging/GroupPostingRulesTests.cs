using Lms.Domain.Messaging;
using Lms.Infrastructure.Messaging;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared.Messaging;

namespace Lms.IntegrationTests.Messaging;

/// <summary>Who may post in a group (M6 Task 10, D74 students never, D77 guardians only in a teacher-run group, staff always).</summary>
[Collection(ApiCollection.Name)]
public sealed class GroupPostingRulesTests(LmsApiFactory factory)
{
    private sealed record Folks(Guid Staff, Guid Student, Guid Guardian, Guid Nobody, Guid Org, Guid ClassCourse);

    private async Task<Folks> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var staff = await PeopleTestKit.AddStaffAsync(factory, tree.School1);
        var student = await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode());
        var studentUser = Guid.NewGuid();
        var guardianUser = Guid.NewGuid();
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var tracked = await db.Set<Lms.Domain.People.StudentProfile>().FindAsync(student.Id);
            tracked!.LinkUser(studentUser);
            await db.SaveChangesAsync();
        }

        await PeopleTestKit.AddGuardianAsync(factory, tree.Org.Id, PeopleTestKit.NewMobile(), userId: guardianUser);
        return new Folks(staff.UserId, studentUser, guardianUser, Guid.NewGuid(), tree.Org.Id, Guid.NewGuid());
    }

    private async Task<string?> ReasonAsync(Conversation conversation, Guid user)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await new GroupPostingRules(db).BlockReasonAsync(conversation, user, CancellationToken.None);
    }

    private static Conversation Group(Folks p, bool readOnly = false, Guid? classCourse = null)
    {
        var group = Conversation.Group(p.Org, p.Staff, "G", classCourse, DateTime.UtcNow);
        group.SetReadOnly(readOnly);
        return group;
    }

    [Fact]
    public async Task Staff_post_in_every_group_even_when_the_owner_set_it_read_only()
    {
        var p = await SetUpAsync("GP1");

        Assert.Null(await ReasonAsync(Group(p), p.Staff));
        Assert.Null(await ReasonAsync(Group(p, readOnly: true), p.Staff));
        Assert.Null(await ReasonAsync(Group(p, readOnly: true, classCourse: p.ClassCourse), p.Staff));
    }

    [Fact]
    public async Task A_student_never_posts_in_a_group()
    {
        var p = await SetUpAsync("GP2");

        Assert.Equal(ConversationReadOnlyReasons.GroupStudentsReadOnly, await ReasonAsync(Group(p), p.Student));
        Assert.Equal(ConversationReadOnlyReasons.GroupStudentsReadOnly, await ReasonAsync(Group(p, classCourse: p.ClassCourse), p.Student));
        Assert.Equal(ConversationReadOnlyReasons.GroupStudentsReadOnly, await ReasonAsync(Group(p, readOnly: true), p.Student));
    }

    [Fact]
    public async Task A_guardian_posts_only_in_a_teacher_run_group_that_is_not_read_only()
    {
        var p = await SetUpAsync("GP3");

        Assert.Null(await ReasonAsync(Group(p, classCourse: p.ClassCourse), p.Guardian));
        Assert.Equal(ConversationReadOnlyReasons.GroupGuardiansReadOnly, await ReasonAsync(Group(p), p.Guardian)); // a staff group with no class
        Assert.Equal(ConversationReadOnlyReasons.GroupGuardiansReadOnly, await ReasonAsync(Group(p, readOnly: true, classCourse: p.ClassCourse), p.Guardian));
    }

    [Fact]
    public async Task Anyone_else_is_blocked_only_by_the_read_only_switch_and_a_direct_conversation_has_no_group_rule()
    {
        var p = await SetUpAsync("GP4");

        Assert.Null(await ReasonAsync(Group(p), p.Nobody));
        Assert.Equal(ConversationReadOnlyReasons.BlockedOrRule, await ReasonAsync(Group(p, readOnly: true), p.Nobody));
        var direct = Conversation.Direct(p.Org, p.Staff, p.Student, DateTime.UtcNow);
        Assert.Null(await ReasonAsync(direct, p.Student)); // the D69 policy decides a direct conversation
        Assert.Null(await ReasonAsync(direct, p.Guardian));
    }
}
