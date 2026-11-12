using Lms.Api.Features.Classes;
using Lms.Api.Features.Content;
using Lms.Api.Features.Enrollment;
using Lms.Api.Features.Enrollment.Groups;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Domain.Quizzes;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Quizzes;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Quizzes;

/// <summary>
/// The quiz builder (M4a Task 10, FR-QZ-01, FR-QZ-02): quizzes and their questions under a class course. Every call needs
/// Quizzes.Manage and the relationship to the class course; every write goes through the closed class / year guard. Once a
/// student has started an attempt the correct options, points, question set and pool are locked (409 <c>quiz_has_attempts</c>).
/// </summary>
public sealed class QuizBuilderHandlers(
    LmsDbContext db, QuizMapper mapper, IModuleWriter modules, ModuleAccess access, IClock clock, PlacementGuard guard, AttemptService attempts)
{
    public async Task<IResult> ListAsync(Guid classCourseId, int page, int pageSize, HttpContext http, CancellationToken ct)
    {
        if (page < 1 || pageSize < 1 || pageSize > Paging.MaxPageSize)
        {
            return ClassValidators.Invalid(page < 1 ? nameof(page) : nameof(pageSize), "paging_invalid");
        }

        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Quizzes.View, classCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var now = clock.UtcNow;
        var isStudent = await access.IsStudentAsync(ct);
        Guid? studentId = null;
        var visibleModuleIds = new HashSet<Guid>();
        if (isStudent)
        {
            studentId = await access.StudentIdAsync(ct);
            if (studentId is null)
            {
                return Results.Ok(new PagedResult<QuizListItemDto>([], 0, page, pageSize));
            }

            var group = await access.GroupOfAsync(classCourseId, studentId.Value, ct);
            visibleModuleIds = (await access.VisibleTo(classCourseId, group, now).Select(p => p.Module.Id).ToListAsync(ct)).ToHashSet();
        }

        var rows = (await (from q in db.Set<Quiz>()
                           join m in db.Set<LessonModule>() on q.ModuleId equals m.Id
                           where q.ClassCourseId == cc!.Id
                           orderby m.Order, q.Id
                           select new { Quiz = q, Module = m }).ToListAsync(ct))
            .Where(r => !isStudent || visibleModuleIds.Contains(r.Module.Id))
            .ToList();
        var pageRows = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var ids = pageRows.Select(r => r.Quiz.Id).ToList();
        var questionCounts = await db.Set<QuizQuestion>().Where(q => ids.Contains(q.QuizId)).GroupBy(q => q.QuizId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var attemptCounts = await db.Set<QuizAttempt>().Where(a => ids.Contains(a.QuizId) && (studentId == null || a.StudentId == studentId))
            .GroupBy(a => a.QuizId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var scores = new Dictionary<Guid, decimal?>();
        foreach (var r in pageRows.Where(r => studentId is not null && r.Quiz.ReviewPolicy != Lms.Shared.Quizzes.ReviewPolicies.None))
        {
            // The student score follows the review policy: with None it is not shown.
            var total = await db.Set<QuizQuestion>().Where(q => q.QuizId == r.Quiz.Id).SumAsync(q => (decimal?)q.Points, ct) ?? 0m;
            var graded = await attempts.GradedAttemptsAsync(r.Quiz.Id, studentId!.Value, ct);
            scores[r.Quiz.Id] = QuizScoring.Final(graded, r.Quiz.GradingMethod, total)?.Points;
        }

        var items = pageRows.Select(r => new QuizListItemDto(
            r.Quiz.Id, r.Module.Id, r.Quiz.LessonKey, r.Module.TitleAr, r.Module.TitleEn, r.Module.Visibility, r.Module.IsVisibleNow(now),
            ModuleMapper.Utc(r.Quiz.OpensAtUtc), ModuleMapper.Utc(r.Quiz.ClosesAtUtc), questionCounts.GetValueOrDefault(r.Quiz.Id),
            isStudent ? null : attemptCounts.GetValueOrDefault(r.Quiz.Id), isStudent ? attemptCounts.GetValueOrDefault(r.Quiz.Id) : null, scores.GetValueOrDefault(r.Quiz.Id))).ToList();
        return Results.Ok(new PagedResult<QuizListItemDto>(items, rows.Count, page, pageSize));
    }

    public async Task<IResult> CreateAsync(Guid classCourseId, CreateQuizRequest? request, HttpContext http, CancellationToken ct)
    {
        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Quizzes.Manage, classCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        // The quiz also creates a lesson module: the caller needs Modules.Manage on the class course too.
        if ((await http.CheckClassCourseAsync(Permissions.Modules.Manage, classCourseId, ct)).Problem is { } noModules)
        {
            return noModules;
        }

        if (QuizValidators.Validate(request) is { } invalid)
        {
            return invalid;
        }

        if (QuizValidators.PoolProblem(request!.Settings.DrawCount, 0) is { } pool)
        {
            return pool; // a new quiz has no questions yet
        }

        if (!await LessonExistsAsync(cc!, request.LessonKey, ct))
        {
            return ApiProblems.NotFound();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var quiz = new Quiz(cc!, request.LessonKey);
        try
        {
            var moduleId = await modules.CreateForRefAsync(
                classCourseId, request.LessonKey, Lms.Shared.Content.ModuleKinds.Quiz, quiz.Id, QuizValidators.Metadata(request.Module), ct);
            quiz.AttachModule(moduleId);
        }
        catch (ModuleRuleException rule)
        {
            return ApiProblems.BadRequest(rule.Code);
        }
        catch (EnrollmentRuleException rule)
        {
            return EnrollmentProblems.From(rule);
        }

        Apply(quiz, request.DescriptionAr, request.DescriptionEn, request.Settings);
        db.Add(quiz);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created(Routes.Quiz(quiz.Id), await mapper.BuilderAsync(quiz, true, clock.UtcNow, ct));
    }

    public async Task<IResult> GetAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var (quiz, _, problem) = await ReachAsync(id, Permissions.Quizzes.Manage, http, ct);
        return problem ?? Results.Ok(await mapper.BuilderAsync(quiz!, true, clock.UtcNow, ct));
    }

    /// <summary>The quiz as a student would see one attempt (drawn and shuffled like a start, no correct options); no attempt is created.</summary>
    public async Task<IResult> PreviewAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var (quiz, _, problem) = await ReachAsync(id, Permissions.Quizzes.Manage, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        var questions = await db.Set<QuizQuestion>().Where(q => q.QuizId == id).ToListAsync(ct);
        var options = await db.Set<QuizOption>().Where(o => o.QuizId == id).ToListAsync(ct);
        var drawn = QuizDraw.Draw(quiz!, questions, options, Random.Shared);
        return Results.Ok(new QuizPreviewDto(id, quiz!.TimeLimitMinutes, QuizMapper.AttemptQuestions(drawn), clock.UtcNow));
    }

    public async Task<IResult> UpdateAsync(Guid id, UpdateQuizRequest? request, HttpContext http, CancellationToken ct)
    {
        var (quiz, _, problem) = await ReachAsync(id, Permissions.Quizzes.Manage, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        if ((await http.CheckClassCourseAsync(Permissions.Modules.Manage, quiz!.ClassCourseId, ct)).Problem is { } noModules)
        {
            return noModules;
        }

        if (QuizValidators.Validate(request) is { } invalid)
        {
            return invalid;
        }

        var questionCount = await db.Set<QuizQuestion>().CountAsync(q => q.QuizId == id, ct);
        if (QuizValidators.PoolProblem(request!.Settings.DrawCount, questionCount) is { } pool)
        {
            return pool;
        }

        // A smaller pool changes what an attempt draws; the other settings stay editable.
        if (await HasAttemptsAsync(id, ct) && Smaller(request.Settings.DrawCount, quiz.DrawCount, questionCount))
        {
            return ApiProblems.Conflict(ProblemCodes.quiz_has_attempts);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await modules.UpdateMetadataAsync(quiz.ModuleId, QuizValidators.Metadata(request.Module), ct);
        }
        catch (ModuleRuleException rule)
        {
            return ApiProblems.BadRequest(rule.Code);
        }
        catch (EnrollmentRuleException rule)
        {
            return EnrollmentProblems.From(rule);
        }

        Apply(quiz, request.DescriptionAr, request.DescriptionEn, request.Settings);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(await mapper.BuilderAsync(quiz, true, clock.UtcNow, ct));
    }

    public async Task<IResult> AddQuestionAsync(Guid id, QuizQuestionInput? input, HttpContext http, CancellationToken ct)
    {
        var (quiz, cc, problem) = await ReachAsync(id, Permissions.Quizzes.Manage, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (QuizValidators.Validate(input) is { } invalid)
        {
            return invalid;
        }

        if (await Locked(quiz!, cc!, ct) is { } locked)
        {
            return locked;
        }

        var last = await db.Set<QuizQuestion>().Where(q => q.QuizId == id).MaxAsync(q => (int?)q.Order, ct) ?? 0;
        var question = new QuizQuestion(quiz!, last + 1, input!.Type, input.Text.Trim(), input.Points);
        db.Add(question);
        var options = AddOptions(question, input.Options);
        await db.SaveChangesAsync(ct);
        return Results.Created(Routes.QuizQuestion(question.Id), QuizMapper.Question(question, options));
    }

    public async Task<IResult> UpdateQuestionAsync(Guid questionId, QuizQuestionInput? input, HttpContext http, CancellationToken ct)
    {
        var (question, quiz, cc, problem) = await ReachQuestionAsync(questionId, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (QuizValidators.Validate(input) is { } invalid)
        {
            return invalid;
        }

        if (await GroupEndpoints.WritableAsync(cc!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        var current = await db.Set<QuizOption>().Where(o => o.QuestionId == questionId).OrderBy(o => o.Order).ThenBy(o => o.Id).ToListAsync(ct);
        var options = input!.Options;
        if (await HasAttemptsAsync(quiz!.Id, ct))
        {
            // Only texts may change: the type, the points and which options are correct stay as they were.
            var same = input.Type == question!.Type && input.Points == question.Points && options.Count == current.Count
                       && current.Zip(options).All(p => p.First.IsCorrect == p.Second.IsCorrect);
            if (!same)
            {
                return ApiProblems.Conflict(ProblemCodes.quiz_has_attempts);
            }

            question.Apply(question.Type, input.Text.Trim(), question.Points);
            for (var i = 0; i < current.Count; i++)
            {
                current[i].Apply(current[i].Order, options[i].Text.Trim(), current[i].IsCorrect);
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(QuizMapper.Question(question, current));
        }

        question!.Apply(input.Type, input.Text.Trim(), input.Points);
        db.RemoveRange(current);
        var replaced = AddOptions(question, options);
        await db.SaveChangesAsync(ct);
        return Results.Ok(QuizMapper.Question(question, replaced));
    }

    public async Task<IResult> DeleteQuestionAsync(Guid questionId, HttpContext http, CancellationToken ct)
    {
        var (question, quiz, cc, problem) = await ReachQuestionAsync(questionId, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await Locked(quiz!, cc!, ct) is { } locked)
        {
            return locked;
        }

        var remaining = await db.Set<QuizQuestion>().CountAsync(q => q.QuizId == quiz!.Id && q.Id != questionId, ct);
        if (quiz!.DrawCount is { } draw && draw > remaining)
        {
            return ApiProblems.BadRequest(ProblemCodes.pool_draw_invalid); // lower the pool first
        }

        db.RemoveRange(await db.Set<QuizOption>().Where(o => o.QuestionId == questionId).ToListAsync(ct));
        db.Remove(question!);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public async Task<IResult> ReorderQuestionsAsync(Guid id, ReorderQuizQuestionsRequest? request, HttpContext http, CancellationToken ct)
    {
        var (quiz, cc, problem) = await ReachAsync(id, Permissions.Quizzes.Manage, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        var questions = await db.Set<QuizQuestion>().Where(q => q.QuizId == id).ToListAsync(ct);
        var ids = request?.QuestionIds;
        if (ids is null || ids.Count != questions.Count || ids.Distinct().Count() != ids.Count || !questions.All(q => ids.Contains(q.Id)))
        {
            return ClassValidators.Invalid(nameof(ReorderQuizQuestionsRequest.QuestionIds), "set_mismatch");
        }

        if (await GroupEndpoints.WritableAsync(cc!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        for (var i = 0; i < ids.Count; i++)
        {
            questions.Single(q => q.Id == ids[i]).SetOrder(i + 1);
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(await mapper.QuestionsAsync(quiz!.Id, ct));
    }

    private async Task<(Quiz? Quiz, ClassCourse? Cc, IResult? Problem)> ReachAsync(Guid id, string permission, HttpContext http, CancellationToken ct)
    {
        var quiz = await db.Set<Quiz>().FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quiz is null)
        {
            return (null, null, ApiProblems.NotFound());
        }

        var (cc, problem) = await http.CheckClassCourseAsync(permission, quiz.ClassCourseId, ct);
        return problem is null ? (quiz, cc, null) : (null, null, problem);
    }

    private async Task<(QuizQuestion? Question, Quiz? Quiz, ClassCourse? Cc, IResult? Problem)> ReachQuestionAsync(
        Guid questionId, HttpContext http, CancellationToken ct)
    {
        var question = await db.Set<QuizQuestion>().FirstOrDefaultAsync(q => q.Id == questionId, ct);
        if (question is null)
        {
            return (null, null, null, ApiProblems.NotFound());
        }

        var (quiz, cc, problem) = await ReachAsync(question.QuizId, Permissions.Quizzes.Manage, http, ct);
        return problem is null ? (question, quiz, cc, null) : (null, null, null, problem);
    }

    /// <summary>Adding or deleting a question after the first attempt, or in a closed class, is refused.</summary>
    private async Task<IResult?> Locked(Quiz quiz, ClassCourse cc, CancellationToken ct)
    {
        if (await GroupEndpoints.WritableAsync(cc, db, guard, ct) is { } closed)
        {
            return closed;
        }

        return await HasAttemptsAsync(quiz.Id, ct) ? ApiProblems.Conflict(ProblemCodes.quiz_has_attempts) : null;
    }

    private Task<bool> HasAttemptsAsync(Guid quizId, CancellationToken ct) => db.Set<QuizAttempt>().AnyAsync(a => a.QuizId == quizId, ct);

    private Task<bool> LessonExistsAsync(ClassCourse cc, Guid lessonKey, CancellationToken ct) =>
        (from p in db.Set<ClassLessonPlan>() join l in db.Set<ClassLesson>() on p.Id equals l.PlanId
         where p.ClassCourseId == cc.Id && l.Key == lessonKey select l.Id).AnyAsync(ct);

    private List<QuizOption> AddOptions(QuizQuestion question, IReadOnlyList<QuizOptionInput> inputs)
    {
        var options = inputs.Select((o, i) => new QuizOption(question, i + 1, o.Text.Trim(), o.IsCorrect)).ToList();
        db.AddRange(options);
        return options;
    }

    private static bool Smaller(int? requested, int? stored, int questionCount) => (requested ?? questionCount) < (stored ?? questionCount);

    private static void Apply(Quiz quiz, string? descriptionAr, string? descriptionEn, QuizSettingsDto s) =>
        quiz.Apply(
            Clean(descriptionAr), Clean(descriptionEn), s.TimeLimitMinutes, s.MaxAttempts, s.GradingMethod, s.ShuffleQuestions, s.ShuffleOptions,
            s.OpensAtUtc, s.ClosesAtUtc, s.ReviewPolicy, s.DrawCount);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
