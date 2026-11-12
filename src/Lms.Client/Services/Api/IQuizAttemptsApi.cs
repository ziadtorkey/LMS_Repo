using Lms.Shared;
using Lms.Shared.Quizzes;

namespace Lms.Client.Services.Api;

/// <summary>Quiz attempts: the student's start page, attempt, autosave and result, and the staff's attempts list and essay grading (FR-QZ-03).</summary>
public interface IQuizAttemptsApi
{
    Task<QuizSummaryDto> GetSummaryAsync(Guid quizId, CancellationToken ct = default);

    /// <summary>POST /api/quizzes/{id}/attempts — starts an attempt, or returns the one already in progress.</summary>
    Task<AttemptDto> StartAsync(Guid quizId, CancellationToken ct = default);

    /// <summary>GET /api/quiz-attempts/{id} — resume.</summary>
    Task<AttemptDto> GetAsync(Guid attemptId, CancellationToken ct = default);

    /// <summary>PUT /api/quiz-attempts/{id}/answers — autosave one answer (409 <c>attempt_expired</c> once the attempt has ended).</summary>
    Task<SaveAnswerResultDto> SaveAnswerAsync(Guid attemptId, SaveAnswerRequest request, CancellationToken ct = default);

    /// <summary>POST /api/quiz-attempts/{id}/submit — idempotent.</summary>
    Task<AttemptResultDto> SubmitAsync(Guid attemptId, CancellationToken ct = default);

    /// <summary>GET /api/quiz-attempts/{id}/result — content follows the review policy for students; staff see everything.</summary>
    Task<AttemptResultDto> GetResultAsync(Guid attemptId, CancellationToken ct = default);

    /// <summary>GET /api/quizzes/{id}/attempts — staff, paged.</summary>
    Task<PagedResult<AttemptSummaryDto>> ListAttemptsAsync(Guid quizId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>PUT /api/quiz-attempts/{id}/grade — essay points and feedback.</summary>
    Task<AttemptResultDto> GradeAsync(Guid attemptId, GradeAttemptRequest request, CancellationToken ct = default);
}

public sealed class QuizAttemptsApi(ApiHttp http) : IQuizAttemptsApi
{
    public Task<QuizSummaryDto> GetSummaryAsync(Guid quizId, CancellationToken ct = default) =>
        http.GetAsync<QuizSummaryDto>(Routes.QuizSummary(quizId), ct);

    public async Task<AttemptDto> StartAsync(Guid quizId, CancellationToken ct = default) =>
        (await http.SendAsync<AttemptDto>(HttpMethod.Post, Routes.QuizAttemptsOf(quizId), null, ct))!;

    public Task<AttemptDto> GetAsync(Guid attemptId, CancellationToken ct = default) =>
        http.GetAsync<AttemptDto>(Routes.QuizAttempt(attemptId), ct);

    public async Task<SaveAnswerResultDto> SaveAnswerAsync(Guid attemptId, SaveAnswerRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<SaveAnswerResultDto>(HttpMethod.Put, Routes.QuizAttemptAnswers(attemptId), request, ct))!;

    public async Task<AttemptResultDto> SubmitAsync(Guid attemptId, CancellationToken ct = default) =>
        (await http.SendAsync<AttemptResultDto>(HttpMethod.Post, Routes.QuizAttemptSubmit(attemptId), null, ct))!;

    public Task<AttemptResultDto> GetResultAsync(Guid attemptId, CancellationToken ct = default) =>
        http.GetAsync<AttemptResultDto>(Routes.QuizAttemptResult(attemptId), ct);

    public Task<PagedResult<AttemptSummaryDto>> ListAttemptsAsync(Guid quizId, int page, int pageSize, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<AttemptSummaryDto>>($"{Routes.QuizAttemptsOf(quizId)}?page={page}&pageSize={pageSize}", ct);

    public async Task<AttemptResultDto> GradeAsync(Guid attemptId, GradeAttemptRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<AttemptResultDto>(HttpMethod.Put, Routes.QuizAttemptGrade(attemptId), request, ct))!;
}

/// <summary>
/// In-memory quiz taking for the dev server and tests: one seeded quiz (single choice, multiple choice, essay), a server clock,
/// the sequence rule of autosave, expiry with the grace period, and results by review policy.
/// </summary>
public sealed class FakeQuizAttemptsApi : IQuizAttemptsApi
{
    public static readonly Guid QuizId = Guid.Parse("00000000-0000-0000-0000-0000000b7701");
    public static readonly Guid SingleQuestionId = Guid.Parse("00000000-0000-0000-0000-0000000b7711");
    public static readonly Guid MultiQuestionId = Guid.Parse("00000000-0000-0000-0000-0000000b7712");
    public static readonly Guid EssayQuestionId = Guid.Parse("00000000-0000-0000-0000-0000000b7713");
    public static readonly Guid CorrectOptionId = Guid.Parse("00000000-0000-0000-0000-0000000b7721");
    public static readonly Guid WrongOptionId = Guid.Parse("00000000-0000-0000-0000-0000000b7722");
    public const int GraceSeconds = 10;

    private sealed class Attempt
    {
        public Guid Id = Guid.NewGuid();
        public int Number;
        public string Status = AttemptStatuses.InProgress;
        public DateTime StartedAt;
        public DateTime? EndsAt;
        public DateTime? SubmittedAt;
        public Dictionary<Guid, AttemptAnswerDto> Answers = [];
        public Dictionary<Guid, (decimal Points, string? Feedback)> EssayGrades = [];
    }

    private readonly List<Attempt> _attempts = [];

    public Func<DateTime> ServerNow { get; set; } = () => DateTime.UtcNow;
    public int? TimeLimitMinutes { get; set; } = 30;
    public int? MaxAttempts { get; set; } = 2;
    public string ReviewPolicy { get; set; } = ReviewPolicies.AnswersAndCorrect;
    public DateTime? ClosesAtUtc { get; set; }
    public List<SaveAnswerRequest> Saves { get; } = [];
    /// <summary>Saves that fail with a network error before the next one succeeds.</summary>
    public int SaveFailuresLeft { get; set; }
    public List<string> Calls { get; } = [];
    public List<GradeAttemptRequest> GradeRequests { get; } = [];

    private static readonly IReadOnlyList<AttemptQuestionDto> Questions =
    [
        new(SingleQuestionId, 1, QuestionTypes.SingleChoice, "Which letter comes first?", 2m,
            [new(CorrectOptionId, "A"), new(WrongOptionId, "B")]),
        new(MultiQuestionId, 2, QuestionTypes.MultipleChoice, "Pick the vowels", 2m,
            [new(Guid.Parse("00000000-0000-0000-0000-0000000b7731"), "A"), new(Guid.Parse("00000000-0000-0000-0000-0000000b7732"), "B"),
             new(Guid.Parse("00000000-0000-0000-0000-0000000b7733"), "E")]),
        new(EssayQuestionId, 3, QuestionTypes.Essay, "Describe the letter A.", 3m, []),
    ];

    private static readonly HashSet<Guid> Correct = [CorrectOptionId, Guid.Parse("00000000-0000-0000-0000-0000000b7731"), Guid.Parse("00000000-0000-0000-0000-0000000b7733")];

    private static ApiProblemException Problem(string code, int status = 409) => new(status, code, null, null);

    private Attempt Find(Guid id) => _attempts.FirstOrDefault(a => a.Id == id) ?? throw Problem(ProblemCodes.not_found, 404);

    private int Remaining(Attempt a) => a.EndsAt is { } e ? Math.Max(0, (int)Math.Ceiling((e - ServerNow()).TotalSeconds)) : int.MaxValue;

    private AttemptDto Dto(Attempt a) => new(a.Id, QuizId, a.Number, a.Status, a.StartedAt, a.EndsAt,
        a.EndsAt is null ? null : Remaining(a), ServerNow(), Questions, a.Answers.Values.ToList());

    private bool Ended(Attempt a) => a.Status != AttemptStatuses.InProgress ||
        (a.EndsAt is { } e && ServerNow() > e.AddSeconds(GraceSeconds));

    private void AutoSubmitIfExpired(Attempt a)
    {
        if (a.Status == AttemptStatuses.InProgress && a.EndsAt is { } e && ServerNow() > e.AddSeconds(GraceSeconds))
        {
            a.Status = AttemptStatuses.AutoSubmitted;
            a.SubmittedAt = e;
        }
    }

    private decimal AutoScore(Attempt a)
    {
        decimal score = 0;
        foreach (var q in Questions.Where(q => q.Type != QuestionTypes.Essay))
        {
            var chosen = a.Answers.GetValueOrDefault(q.QuestionId)?.SelectedOptionIds.ToHashSet() ?? [];
            var right = q.Options.Where(o => Correct.Contains(o.Id)).Select(o => o.Id).ToHashSet();
            if (chosen.SetEquals(right)) score += q.Points;
        }
        return score + a.EssayGrades.Values.Sum(g => g.Points);
    }

    private AttemptResultDto Result(Attempt a, bool staff)
    {
        var maxScore = Questions.Sum(q => q.Points);
        var pending = Questions.Any(q => q.Type == QuestionTypes.Essay && !a.EssayGrades.ContainsKey(q.QuestionId));
        var policy = staff ? ReviewPolicies.AnswersAndCorrect : ReviewPolicy;
        decimal? score = policy == ReviewPolicies.None || pending ? null : AutoScore(a);
        IReadOnlyList<AttemptResultQuestionDto>? questions = null;
        if (policy is ReviewPolicies.Answers or ReviewPolicies.AnswersAndCorrect)
        {
            questions = Questions.Select(q =>
            {
                var answer = a.Answers.GetValueOrDefault(q.QuestionId);
                var chosen = answer?.SelectedOptionIds.ToHashSet() ?? [];
                var right = q.Options.Where(o => Correct.Contains(o.Id)).Select(o => o.Id).ToHashSet();
                decimal? awarded = q.Type == QuestionTypes.Essay
                    ? a.EssayGrades.TryGetValue(q.QuestionId, out var g) ? g.Points : null
                    : chosen.SetEquals(right) ? q.Points : 0m;
                return new AttemptResultQuestionDto(q.QuestionId, q.Order, q.Type, q.Text, q.Points, awarded,
                    q.Options.Select(o => new AttemptResultOptionDto(o.Id, o.Text, chosen.Contains(o.Id),
                        policy == ReviewPolicies.AnswersAndCorrect ? Correct.Contains(o.Id) : null)).ToList(),
                    q.Type == QuestionTypes.Essay ? answer?.Text : null,
                    a.EssayGrades.TryGetValue(q.QuestionId, out var gr) ? gr.Feedback : null);
            }).ToList();
        }
        return new AttemptResultDto(a.Id, QuizId, a.Number, a.Status, policy, score, maxScore, pending, questions, ServerNow());
    }

    private Task<T> Run<T>(string call, Func<T> action)
    {
        Calls.Add(call);
        try { return Task.FromResult(action()); }
        catch (Exception ex) when (ex is ApiProblemException or HttpRequestException) { return Task.FromException<T>(ex); }
    }

    public Task<QuizSummaryDto> GetSummaryAsync(Guid quizId, CancellationToken ct = default) =>
        Run("summary", () =>
        {
            foreach (var a in _attempts) AutoSubmitIfExpired(a);
            var now = ServerNow();
            var open = ClosesAtUtc is null || now < ClosesAtUtc;
            var used = _attempts.Count;
            var inProgress = _attempts.FirstOrDefault(a => a.Status == AttemptStatuses.InProgress);
            var mine = _attempts.Select(a => new AttemptSummaryDto(a.Id, Guid.Empty, "أنا", "Me", a.Number, a.Status, a.StartedAt, a.SubmittedAt,
                ReviewPolicy == ReviewPolicies.None ? null : AutoScore(a), Questions.Sum(q => q.Points), false)).ToList();
            return new QuizSummaryDto(QuizId, Guid.NewGuid(), "اختبار الحروف", "Letters quiz", null, "Five minutes on letters.", Questions.Count,
                TimeLimitMinutes, MaxAttempts, used, MaxAttempts is { } m ? Math.Max(0, m - used) : null, null, ClosesAtUtc, GradingMethods.Highest,
                ReviewPolicy, open, inProgress?.Id, mine, null, now);
        });

    public Task<AttemptDto> StartAsync(Guid quizId, CancellationToken ct = default) =>
        Run("start", () =>
        {
            foreach (var a in _attempts) AutoSubmitIfExpired(a);
            if (_attempts.FirstOrDefault(a => a.Status == AttemptStatuses.InProgress) is { } running) return Dto(running);
            var now = ServerNow();
            if (ClosesAtUtc is { } c && now >= c) throw Problem(ProblemCodes.quiz_closed);
            if (MaxAttempts is { } m && _attempts.Count >= m) throw Problem(ProblemCodes.attempt_limit_reached);
            var ends = new[] { TimeLimitMinutes is { } t ? now.AddMinutes(t) : (DateTime?)null, ClosesAtUtc }.Where(x => x is not null).Min();
            var attempt = new Attempt { Number = _attempts.Count + 1, StartedAt = now, EndsAt = ends };
            _attempts.Add(attempt);
            return Dto(attempt);
        });

    public Task<AttemptDto> GetAsync(Guid attemptId, CancellationToken ct = default) =>
        Run("get", () => { var a = Find(attemptId); AutoSubmitIfExpired(a); return Dto(a); });

    public Task<SaveAnswerResultDto> SaveAnswerAsync(Guid attemptId, SaveAnswerRequest r, CancellationToken ct = default) =>
        Run("save", () =>
        {
            Saves.Add(r);
            if (SaveFailuresLeft > 0) { SaveFailuresLeft--; throw new HttpRequestException("offline"); }
            var a = Find(attemptId);
            if (Ended(a)) { AutoSubmitIfExpired(a); throw Problem(ProblemCodes.attempt_expired); }
            var stored = a.Answers.GetValueOrDefault(r.QuestionId)?.Sequence ?? 0;
            if (r.Sequence > stored) a.Answers[r.QuestionId] = new AttemptAnswerDto(r.QuestionId, r.SelectedOptionIds, r.Text, r.Sequence);
            return new SaveAnswerResultDto(r.QuestionId, Math.Max(stored, r.Sequence), a.EndsAt is null ? null : Remaining(a), ServerNow());
        });

    public Task<AttemptResultDto> SubmitAsync(Guid attemptId, CancellationToken ct = default) =>
        Run("submit", () =>
        {
            var a = Find(attemptId);
            if (a.Status == AttemptStatuses.InProgress) { a.Status = AttemptStatuses.Submitted; a.SubmittedAt = ServerNow(); }
            return Result(a, staff: false);
        });

    public Task<AttemptResultDto> GetResultAsync(Guid attemptId, CancellationToken ct = default) =>
        Run("result", () => Result(Find(attemptId), staff: StaffView));

    /// <summary>Reads results as staff (everything shown), as the grading pages do.</summary>
    public bool StaffView { get; set; }

    public Task<PagedResult<AttemptSummaryDto>> ListAttemptsAsync(Guid quizId, int page, int pageSize, CancellationToken ct = default) =>
        Run("list", () =>
        {
            var items = _attempts.Select(a => new AttemptSummaryDto(a.Id, Guid.NewGuid(), "طالب", "Student " + a.Number, a.Number, a.Status, a.StartedAt, a.SubmittedAt,
                a.Status == AttemptStatuses.InProgress ? null : AutoScore(a), Questions.Sum(q => q.Points),
                a.Status != AttemptStatuses.InProgress && Questions.Any(q => q.Type == QuestionTypes.Essay && !a.EssayGrades.ContainsKey(q.QuestionId)))).ToList();
            return new PagedResult<AttemptSummaryDto>(items, items.Count, page, pageSize);
        });

    public Task<AttemptResultDto> GradeAsync(Guid attemptId, GradeAttemptRequest r, CancellationToken ct = default) =>
        Run("grade", () =>
        {
            GradeRequests.Add(r);
            var a = Find(attemptId);
            foreach (var g in r.Grades)
            {
                var q = Questions.FirstOrDefault(x => x.QuestionId == g.QuestionId && x.Type == QuestionTypes.Essay) ?? throw Problem(ProblemCodes.validation_failed, 400);
                if (g.Points < 0 || g.Points > q.Points) throw Problem(ProblemCodes.validation_failed, 400);
                a.EssayGrades[g.QuestionId] = (g.Points, g.Feedback);
            }
            if (Questions.Where(q => q.Type == QuestionTypes.Essay).All(q => a.EssayGrades.ContainsKey(q.QuestionId))) a.Status = AttemptStatuses.Graded;
            return Result(a, staff: true);
        });

    /// <summary>Test helper: a submitted attempt with answers, so the grading pages have something to grade.</summary>
    public Guid SeedSubmittedAttempt(string essayText = "A is the first letter.")
    {
        var a = new Attempt { Number = _attempts.Count + 1, StartedAt = ServerNow().AddMinutes(-20), Status = AttemptStatuses.Submitted, SubmittedAt = ServerNow().AddMinutes(-5) };
        a.Answers[SingleQuestionId] = new(SingleQuestionId, [CorrectOptionId], null, 1);
        a.Answers[EssayQuestionId] = new(EssayQuestionId, [], essayText, 1);
        _attempts.Add(a);
        return a.Id;
    }
}
