using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.Quizzes;

namespace Lms.Client.Services.Api;

/// <summary>Quizzes of a class course: the builder side (FR-QZ-01, FR-QZ-02). Attempts are added by the student-side tasks.</summary>
public interface IQuizzesApi
{
    Task<PagedResult<QuizListItemDto>> ListAsync(Guid classCourseId, int page, int pageSize, CancellationToken ct = default);
    Task<QuizDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<QuizDto> CreateAsync(Guid classCourseId, CreateQuizRequest request, CancellationToken ct = default);
    Task<QuizDto> UpdateAsync(Guid id, UpdateQuizRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<QuizQuestionDto> AddQuestionAsync(Guid quizId, QuizQuestionInput input, CancellationToken ct = default);
    Task<QuizQuestionDto> UpdateQuestionAsync(Guid questionId, QuizQuestionInput input, CancellationToken ct = default);
    Task DeleteQuestionAsync(Guid questionId, CancellationToken ct = default);
    Task ReorderQuestionsAsync(Guid quizId, IReadOnlyList<Guid> questionIds, CancellationToken ct = default);

    /// <summary>The quiz as one student attempt would show it (drawn and shuffled), without correct options.</summary>
    Task<QuizPreviewDto> PreviewAsync(Guid quizId, CancellationToken ct = default);
}

public sealed class QuizzesApi(ApiHttp http) : IQuizzesApi
{
    public Task<PagedResult<QuizListItemDto>> ListAsync(Guid classCourseId, int page, int pageSize, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<QuizListItemDto>>($"{Routes.ClassCourseQuizzes(classCourseId)}?page={page}&pageSize={pageSize}", ct);

    public Task<QuizDto> GetAsync(Guid id, CancellationToken ct = default) => http.GetAsync<QuizDto>(Routes.Quiz(id), ct);

    public async Task<QuizDto> CreateAsync(Guid classCourseId, CreateQuizRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<QuizDto>(HttpMethod.Post, Routes.ClassCourseQuizzes(classCourseId), request, ct))!;

    public async Task<QuizDto> UpdateAsync(Guid id, UpdateQuizRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<QuizDto>(HttpMethod.Put, Routes.Quiz(id), request, ct))!;

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => http.SendAsync(HttpMethod.Delete, Routes.Quiz(id), null, ct);

    public async Task<QuizQuestionDto> AddQuestionAsync(Guid quizId, QuizQuestionInput input, CancellationToken ct = default) =>
        (await http.SendAsync<QuizQuestionDto>(HttpMethod.Post, Routes.QuizQuestionsOf(quizId), input, ct))!;

    public async Task<QuizQuestionDto> UpdateQuestionAsync(Guid questionId, QuizQuestionInput input, CancellationToken ct = default) =>
        (await http.SendAsync<QuizQuestionDto>(HttpMethod.Put, Routes.QuizQuestion(questionId), input, ct))!;

    public Task DeleteQuestionAsync(Guid questionId, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.QuizQuestion(questionId), null, ct);

    public Task ReorderQuestionsAsync(Guid quizId, IReadOnlyList<Guid> questionIds, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.QuizQuestionsOrder(quizId), new ReorderQuizQuestionsRequest(questionIds), ct);

    public Task<QuizPreviewDto> PreviewAsync(Guid quizId, CancellationToken ct = default) =>
        http.GetAsync<QuizPreviewDto>(Routes.QuizPreview(quizId), ct);
}

/// <summary>
/// In-memory quizzes for the standalone dev server and tests. Applies the server rules the builder depends on: option
/// rules per question type (400 validation_failed), pool size (400 pool_draw_invalid), open before close, and the
/// attempt lock (409 quiz_has_attempts) once <see cref="QuizDto.HasAttempts"/>.
/// </summary>
public sealed class FakeQuizzesApi : IQuizzesApi
{
    public static readonly Guid QuizId = Guid.Parse("00000000-0000-0000-0000-0000000b6601");
    public static readonly Guid LockedQuizId = Guid.Parse("00000000-0000-0000-0000-0000000b6602");

    public Dictionary<Guid, QuizDto> Quizzes { get; } = [];
    public List<string> Calls { get; } = [];
    /// <summary>Makes the next call of that name fail once (a save that stops half way).</summary>
    public Dictionary<string, Exception> FailOnce { get; } = [];
    public Exception? Failure { get; set; }

    public FakeQuizzesApi()
    {
        Quizzes[QuizId] = Make(QuizId, "اختبار الحروف", "Letters quiz", hasAttempts: false);
        Quizzes[LockedQuizId] = Make(LockedQuizId, "اختبار مقفل", "Locked quiz", hasAttempts: true);
    }

    private static QuizDto Make(Guid id, string ar, string en, bool hasAttempts)
    {
        var module = new ModuleNodeDto(Guid.NewGuid(), ModuleKinds.Quiz, 1, ar, en, ModuleVisibilities.Published, null, null,
            ModuleTargets.ClassCourse, null, null, null, CompletionRules.Score, true, null);
        var q1 = new QuizQuestionDto(Guid.NewGuid(), 1, QuestionTypes.SingleChoice, "Which letter comes first?", 2m,
            [new(Guid.NewGuid(), 1, "A", true), new(Guid.NewGuid(), 2, "B", false), new(Guid.NewGuid(), 3, "C", false)]);
        var q2 = new QuizQuestionDto(Guid.NewGuid(), 2, QuestionTypes.Essay, "Describe the letter A.", 3m, []);
        var settings = new QuizSettingsDto(30, 2, GradingMethods.Highest, false, true, null, null, ReviewPolicies.Answers, null);
        return new QuizDto(id, FakeLessonPlansApi.ArabicClassCourseId, Guid.Parse("00000000-0000-0000-0000-0000000000b1"), module, null, null,
            settings, 5m, [q1, q2], hasAttempts ? 4 : 0, hasAttempts, true, DateTime.UtcNow);
    }

    private Task<T> Run<T>(string call, Func<T> action)
    {
        Calls.Add(call);
        if (FailOnce.Remove(call, out var once)) return Task.FromException<T>(once);
        if (Failure is not null) return Task.FromException<T>(Failure);
        try { return Task.FromResult(action()); }
        catch (ApiProblemException ex) { return Task.FromException<T>(ex); }
    }

    private static ApiProblemException Problem(string code, int status = 400) => new(status, code, null, null);

    private static void ValidateOptions(QuizQuestionInput q)
    {
        var count = q.Options.Count;
        var correct = q.Options.Count(o => o.IsCorrect);
        var ok = q.Type switch
        {
            QuestionTypes.SingleChoice => count is >= 2 and <= 10 && correct == 1,
            QuestionTypes.MultipleChoice => count is >= 2 and <= 10 && correct >= 1,
            QuestionTypes.TrueFalse => count == 2 && correct == 1,
            QuestionTypes.Essay => count == 0,
            _ => false,
        };
        if (!ok || string.IsNullOrWhiteSpace(q.Text) || q.Points is < 0.01m or > 1000m || q.Options.Any(o => string.IsNullOrWhiteSpace(o.Text)))
            throw Problem(ProblemCodes.validation_failed);
    }

    private static void ValidateSettings(QuizSettingsDto s, int questionCount)
    {
        if (s.OpensAtUtc is { } o && s.ClosesAtUtc is { } c && o >= c) throw Problem(ProblemCodes.validation_failed);
        if (s.TimeLimitMinutes is < 1 or > 600 || s.MaxAttempts is < 1 or > 20) throw Problem(ProblemCodes.validation_failed);
        if (s.DrawCount is { } d && (d < 1 || d > questionCount)) throw Problem(ProblemCodes.pool_draw_invalid);
    }

    private QuizDto Find(Guid id) => Quizzes.TryGetValue(id, out var q) ? q : throw Problem(ProblemCodes.not_found, 404);

    private QuizDto OwnerOfQuestion(Guid questionId) =>
        Quizzes.Values.FirstOrDefault(q => q.Questions.Any(x => x.Id == questionId)) ?? throw Problem(ProblemCodes.not_found, 404);

    private static QuizDto Retotal(QuizDto q, List<QuizQuestionDto> questions)
    {
        var ordered = questions.Select((x, i) => x with { Order = i + 1 }).ToList();
        return q with { Questions = ordered, TotalPoints = ordered.Sum(x => x.Points) };
    }

    public Task<PagedResult<QuizListItemDto>> ListAsync(Guid classCourseId, int page, int pageSize, CancellationToken ct = default) =>
        Run("list", () =>
        {
            var items = Quizzes.Values.Where(q => q.ClassCourseId == classCourseId)
                .Select(q => new QuizListItemDto(q.Id, q.Module.Id, q.LessonKey, q.Module.TitleAr, q.Module.TitleEn, q.Module.Visibility, q.Module.IsVisibleNow,
                    q.Settings.OpensAtUtc, q.Settings.ClosesAtUtc, q.Questions.Count, q.AttemptCount, null, null)).ToList();
            return new PagedResult<QuizListItemDto>(items.Skip((page - 1) * pageSize).Take(pageSize).ToList(), items.Count, page, pageSize);
        });

    public Task<QuizDto> GetAsync(Guid id, CancellationToken ct = default) => Run("get", () => Find(id));

    public Task<QuizDto> CreateAsync(Guid classCourseId, CreateQuizRequest r, CancellationToken ct = default) =>
        Run("create", () =>
        {
            ValidateSettings(r.Settings, 0);
            var id = Guid.NewGuid();
            var m = r.Module;
            var module = new ModuleNodeDto(Guid.NewGuid(), ModuleKinds.Quiz, 1, m.TitleAr, m.TitleEn, m.Visibility, m.VisibleFromUtc, m.VisibleToUtc,
                m.Target, m.GroupId, null, null, m.CompletionRule, m.Visibility == ModuleVisibilities.Published, null);
            return Quizzes[id] = new QuizDto(id, classCourseId, r.LessonKey, module, r.DescriptionAr, r.DescriptionEn, r.Settings, 0m, [], 0, false, true, DateTime.UtcNow);
        });

    public Task<QuizDto> UpdateAsync(Guid id, UpdateQuizRequest r, CancellationToken ct = default) =>
        Run("update", () =>
        {
            var q = Find(id);
            ValidateSettings(r.Settings, q.Questions.Count);
            if (q.HasAttempts && r.Settings.DrawCount != q.Settings.DrawCount && (r.Settings.DrawCount ?? int.MaxValue) < (q.Settings.DrawCount ?? int.MaxValue))
                throw Problem(ProblemCodes.quiz_has_attempts, 409);
            var m = r.Module;
            var module = q.Module with
            {
                TitleAr = m.TitleAr, TitleEn = m.TitleEn, Visibility = m.Visibility, VisibleFromUtc = m.VisibleFromUtc, VisibleToUtc = m.VisibleToUtc,
                Target = m.Target, GroupId = m.GroupId, CompletionRule = m.CompletionRule, IsVisibleNow = m.Visibility == ModuleVisibilities.Published,
            };
            return Quizzes[id] = q with { Module = module, DescriptionAr = r.DescriptionAr, DescriptionEn = r.DescriptionEn, Settings = r.Settings };
        });

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        Run("delete", () => Quizzes.Remove(id) ? true : throw Problem(ProblemCodes.not_found, 404));

    public Task<QuizQuestionDto> AddQuestionAsync(Guid quizId, QuizQuestionInput input, CancellationToken ct = default) =>
        Run("add-question", () =>
        {
            var q = Find(quizId);
            if (q.HasAttempts) throw Problem(ProblemCodes.quiz_has_attempts, 409);
            ValidateOptions(input);
            var created = ToDto(Guid.NewGuid(), q.Questions.Count + 1, input);
            Quizzes[quizId] = Retotal(q, [.. q.Questions, created]);
            return created;
        });

    public Task<QuizQuestionDto> UpdateQuestionAsync(Guid questionId, QuizQuestionInput input, CancellationToken ct = default) =>
        Run("update-question", () =>
        {
            var q = OwnerOfQuestion(questionId);
            var old = q.Questions.First(x => x.Id == questionId);
            ValidateOptions(input);
            // after the first attempt only texts may change
            if (q.HasAttempts && (input.Type != old.Type || input.Points != old.Points || input.Options.Count != old.Options.Count
                || input.Options.Where((o, i) => o.IsCorrect != old.Options[i].IsCorrect).Any()))
                throw Problem(ProblemCodes.quiz_has_attempts, 409);
            var updated = ToDto(questionId, old.Order, input);
            Quizzes[q.Id] = Retotal(q, q.Questions.Select(x => x.Id == questionId ? updated : x).ToList());
            return updated;
        });

    public Task DeleteQuestionAsync(Guid questionId, CancellationToken ct = default) =>
        Run("delete-question", () =>
        {
            var q = OwnerOfQuestion(questionId);
            if (q.HasAttempts) throw Problem(ProblemCodes.quiz_has_attempts, 409);
            Quizzes[q.Id] = Retotal(q, q.Questions.Where(x => x.Id != questionId).ToList());
            return true;
        });

    public Task ReorderQuestionsAsync(Guid quizId, IReadOnlyList<Guid> questionIds, CancellationToken ct = default) =>
        Run("reorder", () =>
        {
            var q = Find(quizId);
            if (!questionIds.OrderBy(x => x).SequenceEqual(q.Questions.Select(x => x.Id).OrderBy(x => x))) throw Problem(ProblemCodes.validation_failed);
            Quizzes[quizId] = Retotal(q, questionIds.Select(id => q.Questions.First(x => x.Id == id)).ToList());
            return true;
        });

    public Task<QuizPreviewDto> PreviewAsync(Guid quizId, CancellationToken ct = default) =>
        Run("preview", () =>
        {
            var q = Find(quizId);
            var drawn = q.Questions.Take(q.Settings.DrawCount ?? q.Questions.Count)
                .Select(x => new AttemptQuestionDto(x.Id, x.Order, x.Type, x.Text, x.Points, x.Options.Select(o => new AttemptOptionDto(o.Id, o.Text)).ToList()))
                .ToList();
            return new QuizPreviewDto(quizId, q.Settings.TimeLimitMinutes, drawn, DateTime.UtcNow);
        });

    private static QuizQuestionDto ToDto(Guid id, int order, QuizQuestionInput i) =>
        new(id, order, i.Type, i.Text, i.Points, i.Options.Select((o, n) => new QuizOptionDto(Guid.NewGuid(), n + 1, o.Text, o.IsCorrect)).ToList());
}
