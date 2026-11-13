using System.Text.Json;
using Lms.Shared;
using Lms.Shared.Assignments;
using Lms.Shared.Content;
using Lms.Shared.Forums;
using Lms.Shared.Library;
using Lms.Shared.Quizzes;

namespace Lms.UnitTests.Contracts;

/// <summary>M4a contract, wave 2 (plan Task 2): assignment, quiz, library and forum catalogs, DTOs and routes.</summary>
public sealed class ActivityContractTests
{
    private static readonly JsonSerializerOptions Web = JsonSerializerOptions.Web;
    private static readonly Guid Id = Guid.Parse("0198a6f2-0000-7000-8000-00000000000a");
    private static readonly Guid Other = Guid.Parse("0198a6f2-0000-7000-8000-00000000000b");

    [Fact]
    public void Activity_catalogs_match_the_plan()
    {
        Assert.Equal(["Allow", "Penalty", "Block"], LatePolicies.All);
        Assert.Equal(["None", "UntilDue", "UntilGraded"], ResubmissionRules.All);
        Assert.Equal(["Draft", "Submitted", "Late", "Graded", "Returned"], SubmissionStatuses.All);
        Assert.Equal(["Submitted", "Late", "Graded", "Returned"], SubmissionStatuses.HandedIn);
        Assert.Equal(["SingleChoice", "MultipleChoice", "TrueFalse", "Essay"], QuestionTypes.All);
        Assert.Equal(["SingleChoice", "MultipleChoice", "TrueFalse"], QuestionTypes.AutoGraded);
        Assert.Equal(["Highest", "Last", "Average"], GradingMethods.All);
        Assert.Equal(["None", "ScoreOnly", "Answers", "AnswersAndCorrect"], ReviewPolicies.All);
        Assert.Equal(["InProgress", "Submitted", "AutoSubmitted", "Graded"], AttemptStatuses.All);
        Assert.Equal(["Book", "Document", "Video", "Audio", "Link", "Other"], LibraryResourceTypes.All);
    }

    [Fact]
    public void Catalog_values_are_unique_and_subsets_are_inside_their_catalogs()
    {
        foreach (var list in new[]
                 {
                     LatePolicies.All, ResubmissionRules.All, SubmissionStatuses.All, QuestionTypes.All, GradingMethods.All,
                     ReviewPolicies.All, AttemptStatuses.All, LibraryResourceTypes.All,
                 })
        {
            Assert.Equal(list.Count, list.Distinct(StringComparer.Ordinal).Count());
        }

        Assert.All(SubmissionStatuses.HandedIn, s => Assert.Contains(s, SubmissionStatuses.All));
        Assert.DoesNotContain(SubmissionStatuses.Draft, SubmissionStatuses.HandedIn);
        Assert.All(QuestionTypes.AutoGraded, t => Assert.Contains(t, QuestionTypes.All));
        Assert.DoesNotContain(QuestionTypes.Essay, QuestionTypes.AutoGraded);
    }

    [Theory]
    [InlineData(ModuleKinds.Assignment, new[] { "Submit", "Score" })]
    [InlineData(ModuleKinds.Quiz, new[] { "Score", "Submit" })]
    [InlineData(ModuleKinds.LibraryResource, new[] { "None", "Manual", "View" })]
    [InlineData(ModuleKinds.Page, new string[0])]
    [InlineData(ModuleKinds.LiveSession, new string[0])]
    [InlineData(ModuleKinds.Scorm, new[] { "None", "Submit", "Score" })]
    [InlineData(ModuleKinds.H5p, new[] { "None", "Submit", "Score" })]
    public void Activity_kinds_use_the_planned_completion_rules(string kind, string[] expected)
    {
        Assert.Equal(expected, CompletionRules.ForActivity(kind));
        Assert.All(CompletionRules.ForActivity(kind), r => Assert.Contains(r, CompletionRules.All));
    }

    [Fact]
    public void Library_assets_are_owned_by_a_unit()
    {
        Assert.Equal("orgUnitId", AssetUploadForm.OrgUnitId);
        Assert.Equal($"/api/assets/limits?orgUnitId={Id}", Routes.AssetsLimitsForUnit(Id));

        var unitAsset = new AssetDto(Guid.NewGuid(), null, AssetKinds.Document, "book.pdf", "application/pdf", 10, null, true,
            DateTime.UtcNow, Id);
        var courseAsset = new AssetDto(Guid.NewGuid(), Other, AssetKinds.Image, "a.png", "image/png", 10, null, true, DateTime.UtcNow);

        Assert.Equal((null, Id), (RoundTrip(unitAsset).ClassCourseId, RoundTrip(unitAsset).OrgUnitId));
        Assert.Equal((Other, null), (RoundTrip(courseAsset).ClassCourseId, RoundTrip(courseAsset).OrgUnitId));
    }

    [Fact]
    public void Assignment_routes_match_the_plan()
    {
        Assert.Equal("/api/class-courses/{id:guid}/assignments", Routes.ClassCourseAssignmentsPattern);
        Assert.Equal("/api/course-assignments/{id:guid}", Routes.CourseAssignmentPattern);
        Assert.Equal("/api/course-assignments/{id:guid}/submissions", Routes.CourseAssignmentSubmissionsPattern);
        Assert.Equal("/api/course-assignments/{id:guid}/my-submission", Routes.CourseAssignmentMySubmissionPattern);
        Assert.Equal("/api/course-assignments/{id:guid}/submit", Routes.CourseAssignmentSubmitPattern);
        Assert.Equal("/api/submissions/{id:guid}", Routes.SubmissionPattern);
        Assert.Equal("/api/submissions/{id:guid}/grade", Routes.SubmissionGradePattern);
        Assert.Equal("/api/submissions/{id:guid}/return", Routes.SubmissionReturnPattern);

        Assert.Equal($"/api/class-courses/{Id}/assignments", Routes.ClassCourseAssignments(Id));
        Assert.Equal($"/api/course-assignments/{Id}", Routes.CourseAssignment(Id));
        Assert.Equal($"/api/course-assignments/{Id}/submissions", Routes.CourseAssignmentSubmissions(Id));
        Assert.Equal($"/api/course-assignments/{Id}/my-submission", Routes.CourseAssignmentMySubmission(Id));
        Assert.Equal($"/api/course-assignments/{Id}/submit", Routes.CourseAssignmentSubmit(Id));
        Assert.Equal($"/api/submissions/{Id}/grade", Routes.SubmissionGrade(Id));
        Assert.Equal($"/api/submissions/{Id}/return", Routes.SubmissionReturn(Id));
        Assert.NotEqual(Routes.Assignments, Routes.CourseAssignments);
    }

    [Fact]
    public void Quiz_routes_match_the_plan()
    {
        Assert.Equal("/api/class-courses/{id:guid}/quizzes", Routes.ClassCourseQuizzesPattern);
        Assert.Equal("/api/quizzes/{id:guid}", Routes.QuizPattern);
        Assert.Equal("/api/quizzes/{id:guid}/questions", Routes.QuizQuestionsPattern);
        Assert.Equal("/api/quizzes/{id:guid}/questions/order", Routes.QuizQuestionsOrderPattern);
        Assert.Equal("/api/quizzes/{id:guid}/preview", Routes.QuizPreviewPattern);
        Assert.Equal("/api/quizzes/{id:guid}/summary", Routes.QuizSummaryPattern);
        Assert.Equal("/api/quizzes/{id:guid}/attempts", Routes.QuizAttemptsPattern);
        Assert.Equal("/api/quiz-questions/{id:guid}", Routes.QuizQuestionPattern);
        Assert.Equal("/api/quiz-attempts/{id:guid}", Routes.QuizAttemptPattern);
        Assert.Equal("/api/quiz-attempts/{id:guid}/answers", Routes.QuizAttemptAnswersPattern);
        Assert.Equal("/api/quiz-attempts/{id:guid}/submit", Routes.QuizAttemptSubmitPattern);
        Assert.Equal("/api/quiz-attempts/{id:guid}/result", Routes.QuizAttemptResultPattern);
        Assert.Equal("/api/quiz-attempts/{id:guid}/grade", Routes.QuizAttemptGradePattern);

        Assert.Equal($"/api/class-courses/{Id}/quizzes", Routes.ClassCourseQuizzes(Id));
        Assert.Equal($"/api/quizzes/{Id}/questions/order", Routes.QuizQuestionsOrder(Id));
        Assert.Equal($"/api/quizzes/{Id}/preview", Routes.QuizPreview(Id));
        Assert.Equal($"/api/quizzes/{Id}/summary", Routes.QuizSummary(Id));
        Assert.Equal($"/api/quizzes/{Id}/attempts", Routes.QuizAttemptsOf(Id));
        Assert.Equal($"/api/quiz-questions/{Id}", Routes.QuizQuestion(Id));
        Assert.Equal($"/api/quiz-attempts/{Id}/answers", Routes.QuizAttemptAnswers(Id));
        Assert.Equal($"/api/quiz-attempts/{Id}/submit", Routes.QuizAttemptSubmit(Id));
        Assert.Equal($"/api/quiz-attempts/{Id}/result", Routes.QuizAttemptResult(Id));
        Assert.Equal($"/api/quiz-attempts/{Id}/grade", Routes.QuizAttemptGrade(Id));
    }

    [Fact]
    public void Library_and_forum_routes_match_the_plan()
    {
        Assert.Equal("/api/library", Routes.Library);
        Assert.Equal("/api/library/tags", Routes.LibraryTags);
        Assert.Equal("/api/library/{id:guid}", Routes.LibraryResourcePattern);
        Assert.Equal("/api/library/{id:guid}/reader", Routes.LibraryResourceReaderPattern);
        Assert.Equal("/api/library/{id:guid}/download", Routes.LibraryResourceDownloadPattern);
        Assert.Equal("/api/library/{id:guid}/add-to-lesson", Routes.LibraryResourceAddToLessonPattern);
        Assert.Equal($"/api/library/tags?orgUnitId={Id}", Routes.LibraryTagsOf(Id));
        Assert.Equal($"/api/library/{Id}/reader", Routes.LibraryResourceReader(Id));
        Assert.Equal($"/api/library/{Id}/download", Routes.LibraryResourceDownload(Id));
        Assert.Equal($"/api/library/{Id}/add-to-lesson", Routes.LibraryResourceAddToLesson(Id));

        Assert.Equal("/api/class-courses/{id:guid}/forum/threads", Routes.ClassCourseForumThreadsPattern);
        Assert.Equal("/api/class-courses/{id:guid}/announcements", Routes.ClassCourseAnnouncementsPattern);
        Assert.Equal("/api/forum-threads/{id:guid}", Routes.ForumThreadPattern);
        Assert.Equal("/api/forum-threads/{id:guid}/posts", Routes.ForumThreadPostsPattern);
        Assert.Equal("/api/forum-threads/{id:guid}/moderation", Routes.ForumThreadModerationPattern);
        Assert.Equal("/api/forum-posts/{id:guid}", Routes.ForumPostPattern);
        Assert.Equal("/api/announcements/{id:guid}", Routes.AnnouncementPattern);
        Assert.Equal($"/api/class-courses/{Id}/forum/threads", Routes.ClassCourseForumThreads(Id));
        Assert.Equal($"/api/class-courses/{Id}/announcements", Routes.ClassCourseAnnouncements(Id));
        Assert.Equal($"/api/forum-threads/{Id}/posts", Routes.ForumThreadPosts(Id));
        Assert.Equal($"/api/forum-threads/{Id}/moderation", Routes.ForumThreadModeration(Id));
        Assert.Equal($"/api/forum-posts/{Id}", Routes.ForumPost(Id));
        Assert.Equal($"/api/announcements/{Id}", Routes.Announcement(Id));

        StudentContractTests.AssertUniqueApiRoutes();
    }

    [Fact]
    public void Assignment_dtos_round_trip()
    {
        var module = Module(ModuleKinds.Assignment, CompletionRules.Submit);
        var settings = new ModuleSettingsInput("الواجب ١", "Homework 1", ModuleVisibilities.Scheduled,
            new DateTime(2026, 11, 20, 6, 0, 0, DateTimeKind.Utc), null, ModuleTargets.ClassCourse, null, CompletionRules.Submit, null);
        var create = new CreateCourseAssignmentRequest(Id, settings, "<p>اقرأ</p>", "<p>Read</p>", [Other],
            new DateTime(2026, 11, 27, 20, 0, 0, DateTimeKind.Utc), 20m, LatePolicies.Penalty, 10, ResubmissionRules.UntilDue,
            true, true);
        var assignment = new CourseAssignmentDto(Id, Other, Id, module, "<p>اقرأ</p>", null, [], create.DueAtUtc, 20m,
            LatePolicies.Penalty, 10, ResubmissionRules.UntilDue, true, false, 3, 1, true, DateTime.UtcNow);
        var grade = new SubmissionGradeDto(18m, 10, 16.2m, "<p>Good</p>", null, DateTime.UtcNow, null);
        var submission = new SubmissionDto(Other, Id, Guid.NewGuid(), "سارة", "Sara", SubmissionStatuses.Late, "<p>answer</p>", [],
            DateTime.UtcNow, true, 2, grade);
        var mine = new MySubmissionDto(assignment, submission, false, ProblemCodes.resubmission_not_allowed, DateTime.UtcNow);

        Assert.Equal(settings.VisibleFromUtc, RoundTrip(create).Module.VisibleFromUtc);
        Assert.Equal(10, RoundTrip(create).PenaltyPercent);
        Assert.Equal(module, RoundTrip(assignment).Module);
        Assert.Equal(16.2m, RoundTrip(submission).Grade!.FinalGrade);
        Assert.Equal("resubmission_not_allowed", RoundTrip(mine).CannotSubmitCode);
        Assert.False(new SubmitAssignmentRequest("<p>x</p>", []).SaveAsDraft);
        Assert.Equal((1, Paging.DefaultPageSize), (new SubmissionQuery().Page, new SubmissionQuery().PageSize));
        Assert.Null(RoundTrip(new SubmissionListItemDto(Id, "سارة", "Sara", null, null, null, false, null)).SubmissionId);
        Assert.Equal(Other, RoundTrip(new GradeSubmissionRequest(15m, null, Other)).AnnotatedFileAssetId);
    }

    [Fact]
    public void Quiz_dtos_round_trip_and_attempt_views_carry_no_correct_flag()
    {
        var settings = new QuizSettingsDto(30, 2, GradingMethods.Highest, true, true,
            new DateTime(2026, 11, 20, 6, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 21, 6, 0, 0, DateTimeKind.Utc),
            ReviewPolicies.AnswersAndCorrect, 5);
        var question = new QuizQuestionDto(Id, 1, QuestionTypes.SingleChoice, "2 + 2?", 2m,
            [new QuizOptionDto(Other, 1, "4", true), new QuizOptionDto(Guid.NewGuid(), 2, "5", false)]);
        var quiz = new QuizDto(Id, Other, Id, Module(ModuleKinds.Quiz, CompletionRules.Score), null, null, settings, 2m,
            [question], 0, false, true, DateTime.UtcNow);
        var attempt = new AttemptDto(Other, Id, 1, AttemptStatuses.InProgress, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30),
            1800, DateTime.UtcNow, [new AttemptQuestionDto(Id, 1, QuestionTypes.SingleChoice, "2 + 2?", 2m, [new AttemptOptionDto(Other, "4")])],
            [new AttemptAnswerDto(Id, [Other], null, 7)]);
        var result = new AttemptResultDto(Other, Id, 1, AttemptStatuses.Graded, ReviewPolicies.ScoreOnly, 2m, 2m, false, null,
            DateTime.UtcNow);

        Assert.Equal(5, RoundTrip(quiz).Settings.DrawCount);
        Assert.True(RoundTrip(quiz).Questions[0].Options[0].IsCorrect);
        Assert.Equal(1800, RoundTrip(attempt).RemainingSeconds);
        Assert.Equal(7, RoundTrip(attempt).Answers[0].Sequence);
        Assert.Null(RoundTrip(result).Questions);
        Assert.Equal(3L, RoundTrip(new SaveAnswerRequest(Id, [], "essay", 3)).Sequence);
        Assert.Equal(2, RoundTrip(new GradeAttemptRequest([new EssayGradeInput(Id, 2m, "ok")])).Grades[0].Points);

        string[] studentFacing = [nameof(AttemptOptionDto), nameof(AttemptQuestionDto), nameof(AttemptDto), nameof(QuizPreviewDto),
            nameof(QuizSummaryDto)];
        Assert.All(studentFacing, name =>
            Assert.DoesNotContain(typeof(AttemptDto).Assembly.GetTypes().Single(t => t.Name == name).GetProperties(),
                p => p.Name == "IsCorrect"));
    }

    [Fact]
    public void Library_and_forum_dtos_round_trip()
    {
        var resource = new LibraryResourceDto(Id, Other, "المدرسة", "School", true, "كتاب", "Book", null, null,
            LibraryResourceTypes.Book, [Guid.NewGuid()], null, null, null, ["grammar"], null, null, null, false, false,
            DateTime.UtcNow);
        var save = new SaveLibraryResourceRequest(Other, "رابط", "Link", null, null, LibraryResourceTypes.Link, [], null, [],
            null, null, "https://example.org", true);
        var add = new AddLibraryResourceToLessonRequest(Other, Id, new ModuleSettingsInput("كتاب", "Book",
            ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.View, null));
        var thread = new ForumThreadDto(Id, Other, "Question", Guid.NewGuid(), "أحمد", "Ahmed", true, false, 3, DateTime.UtcNow,
            DateTime.UtcNow);
        var post = new ForumPostDto(Other, Id, Guid.NewGuid(), "أحمد", "Ahmed", "<p>hi</p>", [], true, true, true, DateTime.UtcNow, null);
        var announcement = new CourseAnnouncementDto(Id, Other, "Exam", "<p>Sunday</p>", true, Guid.NewGuid(), "م", "T",
            DateTime.UtcNow, null);

        Assert.False(RoundTrip(resource).DownloadAllowed);
        Assert.Equal(["grammar"], RoundTrip(resource).Tags);
        Assert.Equal("https://example.org", RoundTrip(save).Url);
        Assert.Equal(CompletionRules.View, RoundTrip(add).Module.CompletionRule);
        Assert.Equal((Guid?)null, new LibraryQuery().OrgUnitId);
        Assert.True(RoundTrip(new ForumThreadDetailDto(thread, post, true, false)).Thread.IsPinned);
        Assert.True(RoundTrip(new ModerateForumThreadRequest(true, true)).IsLocked);
        Assert.Equal("<p>Sunday</p>", RoundTrip(announcement).BodyHtml);
        Assert.True(RoundTrip(new CreateCourseAnnouncementRequest("Exam", "<p>x</p>", false, true)).Notify);
        Assert.Single(RoundTrip(new CreateForumThreadRequest("Q", "<p>x</p>", [Id])).AttachmentAssetIds);
    }

    private static ModuleNodeDto Module(string kind, string rule) =>
        new(Guid.NewGuid(), kind, 1, "نشاط", "Activity", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse,
            null, null, null, rule, true, null);

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Web), Web)!;
}
