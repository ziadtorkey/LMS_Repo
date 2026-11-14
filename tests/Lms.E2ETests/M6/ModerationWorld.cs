namespace Lms.E2ETests.M6;

/// <summary>
/// DRAFT seeding for the Task 35 scenarios, written when the M6 messaging and moderation API is on main (it needs the real routes in <c>E2EApi</c>). One class course in school S1 with a
/// teacher and a student, a guardian of that student, a second school S2 with its own Org Admin, and one direct conversation (teacher to student) with a message from the teacher.
/// </summary>
public sealed record ModerationWorld(
    string TeacherEmail, string StudentEmail, string GuardianEmail, string OrgAdminEmail, string SiblingAdminEmail, string Password,
    Guid ConversationId, Guid MessageId, Guid TeacherUserId, Guid StudentUserId)
{
    public static Task<ModerationWorld> SeedAsync() =>
        throw new NotImplementedException("needs the M6 messaging API in E2EApi (create world, conversation, message); written with the first live scenario");
}
