using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Classes;
using Lms.Domain.Quizzes;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.Quizzes;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Content.ContentTestHost;
using static Lms.IntegrationTests.Quizzes.QuizTestKit;

namespace Lms.IntegrationTests.Quizzes;

/// <summary>M4a Task 10 (FR-QZ-01, FR-QZ-02): the quiz builder.</summary>
[Collection(ApiCollection.Name)]
public sealed class QuizBuilderTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private Task<HttpResponseMessage> AddRawAsync(Lms.Domain.Access.RoleAssignment caller, Guid quizId, QuizQuestionInput input) =>
        _t.SendAsync(caller, HttpMethod.Post, Routes.QuizQuestionsOf(quizId), input);

    private async Task StartAttemptAsync(ContentWorld w, Guid quizId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var quiz = await db.Set<Quiz>().SingleAsync(q => q.Id == quizId);
        db.Add(new QuizAttempt(quiz, w.StudentProfileId, 1, DateTime.UtcNow, null, 1));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Teacher_creates_a_quiz_with_its_module_in_the_lesson()
    {
        var w = await _t.SetUpAsync("QB1");

        var quiz = await _t.CreateQuizAsync(w.Teacher, w, QuizSettings(30, 2, GradingMethods.Last, ReviewPolicies.Answers));

        Assert.Equal((ModuleKinds.Quiz, CompletionRules.Score, Lesson1), (quiz.Module.Kind, quiz.Module.CompletionRule, quiz.LessonKey));
        Assert.Equal((30, 2, GradingMethods.Last, ReviewPolicies.Answers), (quiz.Settings.TimeLimitMinutes, quiz.Settings.MaxAttempts, quiz.Settings.GradingMethod, quiz.Settings.ReviewPolicy));
        Assert.True(quiz.CanEdit);
        Assert.False(quiz.HasAttempts);
        var module = (await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.Module(quiz.Module.Id))).Content.ReadFromJsonAsync<ModuleDetailDto>())!;
        Assert.Equal(quiz.Id, module.Content.RefId); // the module points at the quiz
        var plan = (await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.ClassCourseLessonPlan(w.ClassCourse.Id))).Content
            .ReadFromJsonAsync<Lms.Shared.Classes.ClassLessonPlanDto>())!;
        Assert.Contains(plan.Sections.Single().Lessons.First().Modules!, m => m.Id == quiz.Module.Id);
    }

    [Fact]
    public async Task Create_needs_an_existing_lesson_and_valid_settings()
    {
        var w = await _t.SetUpAsync("QB2");
        var now = DateTime.UtcNow;

        async Task<HttpResponseMessage> Create(CreateQuizRequest r) => await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ClassCourseQuizzes(w.ClassCourse.Id), r);
        var unknownLesson = await Create(CreateRequest(Guid.NewGuid()));
        var closeBeforeOpen = await Create(CreateRequest(Lesson1, QuizSettings(opens: now.AddDays(1), closes: now)));
        var badTime = await Create(CreateRequest(Lesson1, QuizSettings(timeLimit: 0)));
        var badAttempts = await Create(CreateRequest(Lesson1, QuizSettings(maxAttempts: 21)));
        var badMethod = await Create(CreateRequest(Lesson1, QuizSettings(method: "Median")));
        var badPolicy = await Create(CreateRequest(Lesson1, QuizSettings(review: "Everything")));
        var poolWithoutQuestions = await Create(CreateRequest(Lesson1, QuizSettings(draw: 1)));
        var badRule = await Create(CreateRequest(Lesson1) with { Module = ModuleSettings(rule: CompletionRules.View) });

        Assert.Equal(HttpStatusCode.NotFound, unknownLesson.StatusCode);
        foreach (var bad in new[] { closeBeforeOpen, badTime, badAttempts, badMethod, badPolicy, badRule })
        {
            Assert.Equal(ProblemCodes.validation_failed, await CodeAsync(bad));
        }

        Assert.Equal(ProblemCodes.pool_draw_invalid, await CodeAsync(poolWithoutQuestions));
    }

    [Fact]
    public async Task Single_choice_needs_exactly_one_correct()
    {
        var w = await _t.SetUpAsync("QB3");
        var quiz = await _t.CreateQuizAsync(w.Teacher, w);

        var none = await AddRawAsync(w.Teacher, quiz.Id, Single() with { Options = [new("a", false), new("b", false)] });
        var two = await AddRawAsync(w.Teacher, quiz.Id, Single() with { Options = [new("a", true), new("b", true)] });
        var one = await AddRawAsync(w.Teacher, quiz.Id, Single() with { Options = [new("a", true)] });
        var eleven = await AddRawAsync(w.Teacher, quiz.Id, Single(options: 11));
        var ok = await AddRawAsync(w.Teacher, quiz.Id, Single());

        foreach (var bad in new[] { none, two, one, eleven })
        {
            Assert.Equal(ProblemCodes.validation_failed, await CodeAsync(bad));
        }

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task Multi_choice_needs_two_options_and_one_correct_and_other_types_follow_their_rules()
    {
        var w = await _t.SetUpAsync("QB4");
        var quiz = await _t.CreateQuizAsync(w.Teacher, w);

        var noCorrect = await AddRawAsync(w.Teacher, quiz.Id, Multi(correct: []));
        var single = await AddRawAsync(w.Teacher, quiz.Id, Multi() with { Options = [new("a", true)] });
        var multi = await AddRawAsync(w.Teacher, quiz.Id, Multi(correct: [0, 2]));
        var threeOptionsTf = await AddRawAsync(w.Teacher, quiz.Id, TrueFalse() with { Options = [new("a", true), new("b", false), new("c", false)] });
        var tf = await AddRawAsync(w.Teacher, quiz.Id, TrueFalse());
        var essayWithOptions = await AddRawAsync(w.Teacher, quiz.Id, Essay() with { Options = [new("a", true)] });
        var essay = await AddRawAsync(w.Teacher, quiz.Id, Essay());
        var badPoints = await AddRawAsync(w.Teacher, quiz.Id, Single(points: 0));
        var tooManyPoints = await AddRawAsync(w.Teacher, quiz.Id, Single(points: 1000.01m));
        var badText = await AddRawAsync(w.Teacher, quiz.Id, Single(text: " "));
        var badType = await AddRawAsync(w.Teacher, quiz.Id, Single() with { Type = "Match" });

        foreach (var bad in new[] { noCorrect, single, threeOptionsTf, essayWithOptions, badPoints, tooManyPoints, badText, badType })
        {
            Assert.Equal(ProblemCodes.validation_failed, await CodeAsync(bad));
        }

        Assert.All(new[] { multi, tf, essay }, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var detail = await _t.GetQuizAsync(w.Teacher, quiz.Id);
        Assert.Equal([1, 2, 3], detail.Questions.Select(q => q.Order).ToArray());
        Assert.Equal(2m + 1m + 5m, detail.TotalPoints);
    }

    [Fact]
    public async Task Draw_count_cannot_exceed_the_pool()
    {
        var w = await _t.SetUpAsync("QB5");
        var quiz = await _t.CreateQuizAsync(w.Teacher, w);
        var q1 = await _t.AddQuestionAsync(w.Teacher, quiz.Id, Single("one"));
        await _t.AddQuestionAsync(w.Teacher, quiz.Id, Single("two"));
        Task<HttpResponseMessage> Put(int? draw) => _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.Quiz(quiz.Id),
            new UpdateQuizRequest(ModuleSettings(), null, null, QuizSettings(draw: draw)));

        var tooMany = await Put(3);
        var zero = await Put(0);
        var ok = await Put(2);
        var deleteBelowDraw = await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.QuizQuestion(q1.Id));
        await Put(1);
        var deleteOk = await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.QuizQuestion(q1.Id));

        Assert.Equal(ProblemCodes.pool_draw_invalid, await CodeAsync(tooMany));
        Assert.Equal(ProblemCodes.pool_draw_invalid, await CodeAsync(zero));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(ProblemCodes.pool_draw_invalid, await CodeAsync(deleteBelowDraw));
        Assert.Equal(HttpStatusCode.NoContent, deleteOk.StatusCode);
    }

    [Fact]
    public async Task Close_before_open_is_rejected_on_update_too()
    {
        var w = await _t.SetUpAsync("QB6");
        var quiz = await _t.CreateQuizAsync(w.Teacher, w);
        var now = DateTime.UtcNow;

        var response = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.Quiz(quiz.Id),
            new UpdateQuizRequest(ModuleSettings(), null, null, QuizSettings(opens: now, closes: now.AddMinutes(-5))));

        Assert.Equal(ProblemCodes.validation_failed, await CodeAsync(response));
    }

    [Fact]
    public async Task Edit_after_an_attempt_is_409_for_what_changes_the_grade_and_texts_stay_editable()
    {
        var w = await _t.SetUpAsync("QB7");
        var quiz = await _t.CreateQuizAsync(w.Teacher, w);
        var q = await _t.AddQuestionAsync(w.Teacher, quiz.Id, Single("Q", 2, correct: 0));
        await _t.AddQuestionAsync(w.Teacher, quiz.Id, Single("Q2"));
        await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.Quiz(quiz.Id), new UpdateQuizRequest(ModuleSettings(), null, null, QuizSettings(draw: 2)));
        await StartAttemptAsync(w, quiz.Id);

        var otherCorrect = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.QuizQuestion(q.Id), Single("Q", 2, correct: 1));
        var otherPoints = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.QuizQuestion(q.Id), Single("Q", 3, correct: 0));
        var texts = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.QuizQuestion(q.Id), Single("Fixed typo", 2, correct: 0));
        var add = await AddRawAsync(w.Teacher, quiz.Id, Single());
        var delete = await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.QuizQuestion(q.Id));
        var smallerPool = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.Quiz(quiz.Id), new UpdateQuizRequest(ModuleSettings(), null, null, QuizSettings(draw: 1)));
        var otherSettings = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.Quiz(quiz.Id), new UpdateQuizRequest(ModuleSettings(), "x", "y", QuizSettings(draw: 2, timeLimit: 10)));

        foreach (var locked in new[] { otherCorrect, otherPoints, add, delete, smallerPool })
        {
            Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
            Assert.Equal(ProblemCodes.quiz_has_attempts, await CodeAsync(locked));
        }

        Assert.Equal(HttpStatusCode.OK, texts.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherSettings.StatusCode);
        var detail = await _t.GetQuizAsync(w.Teacher, quiz.Id);
        Assert.True(detail.HasAttempts);
        Assert.Equal(1, detail.AttemptCount);
        Assert.Equal("Fixed typo", detail.Questions.First().Text);
    }

    [Fact]
    public async Task Student_and_other_teachers_cannot_read_or_build_and_a_stranger_gets_404()
    {
        var w = await _t.SetUpAsync("QB8");
        var quiz = await _t.CreateQuizAsync(w.Teacher, w);
        var question = await _t.AddQuestionAsync(w.Teacher, quiz.Id, Single());

        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.Quiz(quiz.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizPreview(quiz.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AddRawAsync(w.Student, quiz.Id, Single())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(w.Student, HttpMethod.Delete, Routes.QuizQuestion(question.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Get, Routes.Quiz(quiz.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AddRawAsync(w.OtherTeacher, quiz.Id, Single())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Put, Routes.QuizQuestion(question.Id), Single())).StatusCode);
    }

    [Fact]
    public async Task Preview_draws_like_a_start_without_correct_options_and_creates_no_attempt()
    {
        var w = await _t.SetUpAsync("QB9");
        var quiz = await _t.CreateQuizAsync(w.Teacher, w);
        for (var i = 0; i < 4; i++)
        {
            await _t.AddQuestionAsync(w.Teacher, quiz.Id, Single("Q" + i));
        }

        await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.Quiz(quiz.Id), new UpdateQuizRequest(ModuleSettings(), null, null,
            QuizSettings(timeLimit: 15, draw: 2, shuffleQuestions: true, shuffleOptions: true)));
        var response = await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.QuizPreview(quiz.Id));

        var preview = (await response.Content.ReadFromJsonAsync<QuizPreviewDto>())!;
        Assert.Equal(2, preview.Questions.Count);
        Assert.Equal(15, preview.TimeLimitMinutes);
        Assert.DoesNotContain("isCorrect", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Set<QuizAttempt>().AnyAsync(a => a.QuizId == quiz.Id));
    }

    [Fact]
    public async Task Reorder_requires_the_exact_question_set()
    {
        var w = await _t.SetUpAsync("QB10");
        var quiz = await _t.CreateQuizAsync(w.Teacher, w);
        var a = await _t.AddQuestionAsync(w.Teacher, quiz.Id, Single("a"));
        var b = await _t.AddQuestionAsync(w.Teacher, quiz.Id, Single("b"));
        var url = Routes.QuizQuestionsOrder(quiz.Id);

        var missing = await _t.SendAsync(w.Teacher, HttpMethod.Put, url, new ReorderQuizQuestionsRequest([a.Id]));
        var foreign = await _t.SendAsync(w.Teacher, HttpMethod.Put, url, new ReorderQuizQuestionsRequest([a.Id, Guid.NewGuid()]));
        var ok = await _t.SendAsync(w.Teacher, HttpMethod.Put, url, new ReorderQuizQuestionsRequest([b.Id, a.Id]));

        Assert.Equal(ProblemCodes.validation_failed, await CodeAsync(missing));
        Assert.Equal(ProblemCodes.validation_failed, await CodeAsync(foreign));
        Assert.Equal([b.Id, a.Id], (await ok.Content.ReadFromJsonAsync<List<QuizQuestionDto>>())!.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task Update_changes_the_module_title_and_visibility_and_a_closed_class_rejects_builds()
    {
        var w = await _t.SetUpAsync("QB11");
        var quiz = await _t.CreateQuizAsync(w.Teacher, w);

        var renamed = (await (await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.Quiz(quiz.Id), new UpdateQuizRequest(
            ModuleSettings("Renamed", ModuleVisibilities.Hidden), "a", "b", QuizSettings()))).Content.ReadFromJsonAsync<QuizDto>())!;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == w.Class.Id)).SetStatus(Class.ClosedStatus);
            await db.SaveChangesAsync();
        }

        var add = await AddRawAsync(w.Teacher, quiz.Id, Single());
        var create = await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ClassCourseQuizzes(w.ClassCourse.Id), CreateRequest(Lesson1));

        Assert.Equal(("Renamed", ModuleVisibilities.Hidden, "b"), (renamed.Module.TitleEn, renamed.Module.Visibility, renamed.DescriptionEn));
        Assert.Equal(ProblemCodes.class_closed, await CodeAsync(add));
        Assert.Equal(ProblemCodes.class_closed, await CodeAsync(create));
    }

    [Fact]
    public async Task List_gives_staff_every_quiz_and_a_student_only_the_visible_ones()
    {
        var w = await _t.SetUpAsync("QB12");
        var shown = await _t.CreateQuizAsync(w.Teacher, w, title: "Shown");
        await _t.CreateQuizAsync(w.Teacher, w, title: "Draft", visibility: ModuleVisibilities.Hidden);
        await _t.AddQuestionAsync(w.Teacher, shown.Id, Single());

        var staff = (await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.ClassCourseQuizzes(w.ClassCourse.Id))).Content
            .ReadFromJsonAsync<PagedResult<QuizListItemDto>>())!;
        var student = (await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.ClassCourseQuizzes(w.ClassCourse.Id))).Content
            .ReadFromJsonAsync<PagedResult<QuizListItemDto>>())!;

        Assert.Equal(2, staff.Total);
        Assert.Equal([shown.Id], student.Items.Select(i => i.Id).ToArray());
        Assert.Equal(1, student.Items.Single().QuestionCount);
        Assert.Null(student.Items.Single().AttemptCount); // staff-only count
        Assert.Equal(0, student.Items.Single().MyAttemptsUsed);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Get, Routes.ClassCourseQuizzes(w.ClassCourse.Id))).StatusCode);
    }
}
