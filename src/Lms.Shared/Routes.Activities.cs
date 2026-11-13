namespace Lms.Shared;

// M4a wave 2: assignments, quizzes, the digital library, forums and announcements (M4a plan Task 2).
// "course-assignments", not "assignments": /api/assignments are the M1 role assignments.
public static partial class Routes
{
    // Assignments (FR-ASG-01, FR-ASG-02)
    public const string ClassCourseAssignmentsPattern = ClassCoursePattern + "/assignments";
    public const string CourseAssignments = "/api/course-assignments";
    public const string CourseAssignmentPattern = CourseAssignments + "/{id:guid}";
    public const string CourseAssignmentSubmissionsPattern = CourseAssignmentPattern + "/submissions";
    public const string CourseAssignmentMySubmissionPattern = CourseAssignmentPattern + "/my-submission";
    public const string CourseAssignmentSubmitPattern = CourseAssignmentPattern + "/submit";
    public const string Submissions = "/api/submissions";
    public const string SubmissionPattern = Submissions + "/{id:guid}";
    public const string SubmissionGradePattern = SubmissionPattern + "/grade";
    public const string SubmissionReturnPattern = SubmissionPattern + "/return";

    public static string ClassCourseAssignments(Guid id) => $"{ClassCourse(id)}/assignments";
    public static string CourseAssignment(Guid id) => $"{CourseAssignments}/{id}";
    public static string CourseAssignmentSubmissions(Guid id) => $"{CourseAssignment(id)}/submissions";
    public static string CourseAssignmentMySubmission(Guid id) => $"{CourseAssignment(id)}/my-submission";
    public static string CourseAssignmentSubmit(Guid id) => $"{CourseAssignment(id)}/submit";
    public static string Submission(Guid id) => $"{Submissions}/{id}";
    public static string SubmissionGrade(Guid id) => $"{Submission(id)}/grade";
    public static string SubmissionReturn(Guid id) => $"{Submission(id)}/return";

    // Quizzes (FR-QZ-01..03)
    public const string ClassCourseQuizzesPattern = ClassCoursePattern + "/quizzes";
    public const string Quizzes = "/api/quizzes";
    public const string QuizPattern = Quizzes + "/{id:guid}";
    public const string QuizQuestionsPattern = QuizPattern + "/questions";
    public const string QuizQuestionsOrderPattern = QuizQuestionsPattern + "/order";
    public const string QuizPreviewPattern = QuizPattern + "/preview";
    public const string QuizSummaryPattern = QuizPattern + "/summary";
    public const string QuizAttemptsPattern = QuizPattern + "/attempts";
    public const string QuizQuestions = "/api/quiz-questions";
    public const string QuizQuestionPattern = QuizQuestions + "/{id:guid}";
    public const string QuizAttempts = "/api/quiz-attempts";
    public const string QuizAttemptPattern = QuizAttempts + "/{id:guid}";
    public const string QuizAttemptAnswersPattern = QuizAttemptPattern + "/answers";
    public const string QuizAttemptSubmitPattern = QuizAttemptPattern + "/submit";
    public const string QuizAttemptResultPattern = QuizAttemptPattern + "/result";
    public const string QuizAttemptGradePattern = QuizAttemptPattern + "/grade";

    public static string ClassCourseQuizzes(Guid id) => $"{ClassCourse(id)}/quizzes";
    public static string Quiz(Guid id) => $"{Quizzes}/{id}";
    public static string QuizQuestionsOf(Guid id) => $"{Quiz(id)}/questions";
    public static string QuizQuestionsOrder(Guid id) => $"{QuizQuestionsOf(id)}/order";
    public static string QuizPreview(Guid id) => $"{Quiz(id)}/preview";
    public static string QuizSummary(Guid id) => $"{Quiz(id)}/summary";
    public static string QuizAttemptsOf(Guid id) => $"{Quiz(id)}/attempts";
    public static string QuizQuestion(Guid id) => $"{QuizQuestions}/{id}";
    public static string QuizAttempt(Guid id) => $"{QuizAttempts}/{id}";
    public static string QuizAttemptAnswers(Guid id) => $"{QuizAttempt(id)}/answers";
    public static string QuizAttemptSubmit(Guid id) => $"{QuizAttempt(id)}/submit";
    public static string QuizAttemptResult(Guid id) => $"{QuizAttempt(id)}/result";
    public static string QuizAttemptGrade(Guid id) => $"{QuizAttempt(id)}/grade";

    // Digital library (FR-LIB-01, D13). /reader streams (protected reader); /download answers a signed URL or 403.
    public const string Library = "/api/library";
    public const string LibraryTags = Library + "/tags";
    public const string LibraryResourcePattern = Library + "/{id:guid}";
    public const string LibraryResourceReaderPattern = LibraryResourcePattern + "/reader";
    public const string LibraryResourceDownloadPattern = LibraryResourcePattern + "/download";
    public const string LibraryResourceAddToLessonPattern = LibraryResourcePattern + "/add-to-lesson";

    public static string LibraryTagsOf(Guid orgUnitId) => $"{LibraryTags}?orgUnitId={orgUnitId}";
    public static string LibraryResource(Guid id) => $"{Library}/{id}";
    public static string LibraryResourceReader(Guid id) => $"{LibraryResource(id)}/reader";
    public static string LibraryResourceDownload(Guid id) => $"{LibraryResource(id)}/download";
    public static string LibraryResourceAddToLesson(Guid id) => $"{LibraryResource(id)}/add-to-lesson";

    // Forums and announcements (FR-FOR-01)
    public const string ClassCourseForumThreadsPattern = ClassCoursePattern + "/forum/threads";
    public const string ClassCourseAnnouncementsPattern = ClassCoursePattern + "/announcements";
    public const string ForumThreads = "/api/forum-threads";
    public const string ForumThreadPattern = ForumThreads + "/{id:guid}";
    public const string ForumThreadPostsPattern = ForumThreadPattern + "/posts";
    public const string ForumThreadModerationPattern = ForumThreadPattern + "/moderation";
    public const string ForumPosts = "/api/forum-posts";
    public const string ForumPostPattern = ForumPosts + "/{id:guid}";
    public const string Announcements = "/api/announcements";
    public const string AnnouncementPattern = Announcements + "/{id:guid}";

    public static string ClassCourseForumThreads(Guid id) => $"{ClassCourse(id)}/forum/threads";
    public static string ClassCourseAnnouncements(Guid id) => $"{ClassCourse(id)}/announcements";
    public static string ForumThread(Guid id) => $"{ForumThreads}/{id}";
    public static string ForumThreadPosts(Guid id) => $"{ForumThread(id)}/posts";
    public static string ForumThreadModeration(Guid id) => $"{ForumThread(id)}/moderation";
    public static string ForumPost(Guid id) => $"{ForumPosts}/{id}";
    public static string Announcement(Guid id) => $"{Announcements}/{id}";
}
