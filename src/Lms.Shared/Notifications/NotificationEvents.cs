using Lms.Shared.Authorization;

namespace Lms.Shared.Notifications;

/// <summary>Delivery channels (FR-NOT-01). M1 delivers InApp only; the others are stored preferences for M6.</summary>
public static class NotificationChannels
{
    public const string InApp = "InApp";
    public const string Email = "Email";
    public const string Sms = "Sms";
    public const string Push = "Push";

    public static IReadOnlyList<string> All { get; } = [InApp, Email, Sms, Push];
}

/// <summary>Where the default recipient roles of an event are looked up, relative to the event's unit.</summary>
public static class RecipientScopes
{
    /// <summary>Role holders at the event's unit and below (M1 behaviour).</summary>
    public const string Subtree = "Subtree";

    /// <summary>Role holders at the event's unit and its ancestors (e.g. admins above a staff member's home unit, D35).</summary>
    public const string AncestorsAndSelf = "AncestorsAndSelf";

    public static IReadOnlyList<string> All { get; } = [Subtree, AncestorsAndSelf];
}

/// <summary>
/// A notification event. <c>DefaultRecipientRoles</c> are built-in role keys reached from the event's unit as
/// <c>RecipientScope</c> says (<see cref="RecipientScopes"/>); empty = the publisher names the recipients (e.g. the
/// assignee). <c>Parameters</c> must all be supplied on publish.
/// </summary>
public sealed record NotificationEventDefinition(
    string Key,
    IReadOnlyList<string> DefaultRecipientRoles,
    IReadOnlyList<string> Parameters,
    string RecipientScope = RecipientScopes.Subtree,
    IReadOnlyList<string>? DefaultChannels = null,
    bool Mandatory = false);

/// <summary>Notification event catalog ("Module.Event" keys). Later milestones append; never rename a key.</summary>
public static class NotificationEvents
{
    public const string AccessRoleAssigned = "Access.RoleAssigned";
    public const string AccessRoleRemoved = "Access.RoleRemoved";
    public const string OrganizationPolicyChanged = "Organization.PolicyChanged";

    // M2 (D35 staff expiry, X-06 imports). Student change-request events arrive with the M3 contract (D23).
    public const string StaffExpiringSoon = "Staff.ExpiringSoon";
    public const string StaffExpired = "Staff.Expired";
    public const string ImportsCompleted = "Imports.Completed";

    // M3 students and academic structure.
    public const string StudentsChangeRequested = "Students.ChangeRequested";
    public const string StudentsChangeRequestDecided = "Students.ChangeRequestDecided";
    public const string TeachingAssigned = "Teaching.Assigned";
    public const string BlueprintsPushed = "Blueprints.Pushed";
    public const string EnrollmentExceptionRequested = "Enrollment.ExceptionRequested";
    public const string EnrollmentExceptionDecided = "Enrollment.ExceptionDecided";
    public const string EnrollmentStudentTransferred = "Enrollment.StudentTransferred";

    // M4a teaching and learning (M4a plan Task 2). Named recipients: the students the activity targets.
    public const string AssignmentsPublished = "Assignments.Published";
    public const string AssignmentsDueSoon = "Assignments.DueSoon";
    public const string AssignmentsGraded = "Assignments.Graded";
    public const string QuizzesOpened = "Quizzes.Opened";
    public const string ForumsAnnouncement = "Forums.Announcement";

    // M4b live sessions (M4b plan Task 1). Named recipients: the students the session module targets.
    public const string LiveReminder = "Live.Reminder";

    // M5 wave 1 (M5 plan Task 1, L26). Named recipients: the student and the linked guardians whose visible grades changed, one per student per publish
    // batch. No grade value and no item title in any notification, log or error.
    public const string GradesPublished = "Grades.Published";

    // M6 wave 1 (#613 section 16: one catalog, keys never renamed). No message text and no grade value in any notice.
    public const string MessagesNewMessage = "Messages.NewMessage";
    public const string AnnouncementsPublished = "Announcements.Published";
    public const string MessagingWarning = "Messaging.Warning";
    public const string MessagingRestricted = "Messaging.Restricted";
    public const string MessagingReportResolved = "Messaging.ReportResolved";
    public const string CommsCapReached = "Comms.CapReached";

    public static IReadOnlyList<NotificationEventDefinition> All { get; } =
    [
        new(AccessRoleAssigned, [], ["RoleNameAr", "RoleNameEn", "OrgUnitNameAr", "OrgUnitNameEn"]),
        new(AccessRoleRemoved, [], ["RoleNameAr", "RoleNameEn", "OrgUnitNameAr", "OrgUnitNameEn"]),
        new(OrganizationPolicyChanged, [BuiltInRoles.OrgAdmin, BuiltInRoles.Manager],
            ["PolicyKey", "OrgUnitNameAr", "OrgUnitNameEn", "State"]),
        new(StaffExpiringSoon, [BuiltInRoles.OrgAdmin, BuiltInRoles.Manager],
            ["StaffNameAr", "StaffNameEn", "ExpiresOn"], RecipientScopes.AncestorsAndSelf),
        new(StaffExpired, [BuiltInRoles.OrgAdmin, BuiltInRoles.Manager],
            ["StaffNameAr", "StaffNameEn", "ExpiresOn"], RecipientScopes.AncestorsAndSelf),
        // Named recipient: the user who requested the import.
        new(ImportsCompleted, [], ["Kind", "Status", "RowCount"]),
        new(StudentsChangeRequested, [BuiltInRoles.OrgAdmin, BuiltInRoles.Manager, BuiltInRoles.Counselor],
            ["StudentNameAr", "StudentNameEn"], RecipientScopes.AncestorsAndSelf),
        // Named recipient: the student who requested the change.
        new(StudentsChangeRequestDecided, [], ["StudentNameAr", "StudentNameEn", "Decision"]),
        // Named recipient: the assigned teacher. Role = TeacherRoles value.
        new(TeachingAssigned, [], ["ClassNameAr", "ClassNameEn", "CourseNameAr", "CourseNameEn", "Role"]),
        // Named recipients: teachers of the classes whose plan changed.
        new(BlueprintsPushed, [], ["CourseNameAr", "CourseNameEn", "Version"]),
        // Named recipients: the course supervisors; without supervisors the Managers above the class.
        new(EnrollmentExceptionRequested, [BuiltInRoles.Manager],
            ["StudentNameAr", "StudentNameEn", "CourseNameAr", "CourseNameEn"], RecipientScopes.AncestorsAndSelf),
        // Named recipient: the requester.
        new(EnrollmentExceptionDecided, [],
            ["StudentNameAr", "StudentNameEn", "CourseNameAr", "CourseNameEn", "Decision"]),
        // Named recipients: guardians of the student and teachers of both classes.
        new(EnrollmentStudentTransferred, [],
            ["StudentNameAr", "StudentNameEn", "FromClassAr", "FromClassEn", "ToClassAr", "ToClassEn", "EffectiveOn"]),
        // Named recipients: students of the ClassCourse (or of the target group) who can see the assignment.
        new(AssignmentsPublished, [], ["TitleAr", "TitleEn", "CourseNameAr", "CourseNameEn"]),
        // Named recipients: targeted students without a submission, once, 24 h before the due time.
        new(AssignmentsDueSoon, [], ["TitleAr", "TitleEn", "CourseNameAr", "CourseNameEn", "DueAt"]),
        // Named recipient: the student whose work was returned with a grade.
        new(AssignmentsGraded, [], ["TitleAr", "TitleEn", "CourseNameAr", "CourseNameEn"]),
        // Named recipients: targeted students when the quiz opens (or is published already open).
        new(QuizzesOpened, [], ["TitleAr", "TitleEn", "CourseNameAr", "CourseNameEn"]),
        // Named recipients: students enrolled in the ClassCourse, when the author asks to notify.
        new(ForumsAnnouncement, [], ["Title", "CourseNameAr", "CourseNameEn"]),
        // Named recipients: targeted students who can see the session module, once, Live.ReminderMinutes before start.
        new(LiveReminder, [], ["TitleAr", "TitleEn", "CourseNameAr", "CourseNameEn", "StartsAtUtc"]),
        // Named recipients: the student and the linked guardians, once per publish batch. Course name only: never a score or an item title.
        new(GradesPublished, [], ["CourseNameAr", "CourseNameEn"]),
        // Named recipients: a participant who has no unread notice of the conversation (throttled: one unread notice per conversation). The sender's name only.
        new(MessagesNewMessage, [], ["SenderNameAr", "SenderNameEn"]),
        // Named recipients: the snapshot of the audience, one event per batch; when the author asked to notify. Title only, never the body.
        new(AnnouncementsPublished, [], ["TitleAr", "TitleEn"]),
        // Named recipient: the sender of a reported message when a moderator warns them. Mandatory (moderation outcomes cannot be switched off).
        new(MessagingWarning, [], [], DefaultChannels: [NotificationChannels.InApp, NotificationChannels.Email], Mandatory: true),
        // Named recipient: a user whose messaging right was restricted until the date. Mandatory.
        new(MessagingRestricted, [], ["UntilUtc"], DefaultChannels: [NotificationChannels.InApp, NotificationChannels.Email], Mandatory: true),
        // Named recipient: the reporter, with the outcome (Dismissed or Actioned) and no detail about the other person. Mandatory.
        new(MessagingReportResolved, [], ["Outcome"], DefaultChannels: [NotificationChannels.InApp], Mandatory: true),
        // Org Admins of the client when a daily cap of a channel is reached.
        new(CommsCapReached, [BuiltInRoles.OrgAdmin], ["Channel"], DefaultChannels: [NotificationChannels.InApp, NotificationChannels.Email]),
    ];

    /// <summary>The definition with this exact (case-sensitive) key, or null.</summary>
    public static NotificationEventDefinition? Find(string key) =>
        All.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal));
}
