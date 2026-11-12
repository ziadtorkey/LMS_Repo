using Lms.Domain.Abstractions;
using Lms.Domain.Quizzes;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Quizzes;

/// <summary>
/// Auto-submits unfinished attempts whose time is up (FR-QZ-02): <c>EndsAtUtc</c> (the time limit, or the close when earlier) or the
/// quiz close, plus the grace period. A recurring system sweep every minute; it loops over clients with the platform context, never with
/// an empty context, and is idempotent (an attempt is submitted once, by a conditional update). A read of a late attempt submits it too,
/// so the sweep is a safety net, not the only path.
/// </summary>
public sealed class QuizCloseJob(LmsDbContext db, AttemptService attempts, IClock clock, QuizOpenedNotifier opened)
{
    public const string RecurringJobId = "quiz-close";
    public const string Cron = "* * * * *";
    private const int Batch = 200;

    /// <summary>The sweep: auto-submits what is due, then sends the opened notifications that came due (the same minute tick).</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var submitted = await SweepAsync(null, ignoreGrace: false, ct);
        await opened.NotifyDueAsync(null, ct);
        return submitted;
    }

    /// <summary>One quiz, now, without the grace: the Development trigger and the E2E demo use it.</summary>
    public Task<int> RunForQuizAsync(Guid quizId, CancellationToken ct = default) => SweepAsync(quizId, ignoreGrace: true, ct);

    private async Task<int> SweepAsync(Guid? quizId, bool ignoreGrace, CancellationToken ct)
    {
        var submitted = 0;
        for (var round = 0; round < 50; round++)
        {
            var now = clock.UtcNow;
            var candidates = await (from a in db.Set<QuizAttempt>()
                                    join q in db.Set<Quiz>() on a.QuizId equals q.Id
                                    where a.Status == QuizAttempt.InProgress && (quizId == null || a.QuizId == quizId)
                                          && ((a.EndsAtUtc != null && a.EndsAtUtc < now) || (q.ClosesAtUtc != null && q.ClosesAtUtc < now))
                                    orderby a.StartedAtUtc
                                    select new { Attempt = a, q.ClosesAtUtc }).Take(Batch).ToListAsync(ct);
            var progressed = 0;
            foreach (var candidate in candidates)
            {
                var attempt = candidate.Attempt;
                var grace = ignoreGrace ? 0 : await attempts.GraceSecondsAsync(attempt.OrgUnitPath, ct);
                var deadline = new[] { attempt.EndsAtUtc, candidate.ClosesAtUtc }.Where(d => d is not null).Min()!.Value.AddSeconds(grace);
                if (now > deadline && await attempts.SubmitAsync(attempt, QuizAttempt.AutoSubmitted, ct))
                {
                    submitted++;
                    progressed++;
                }
            }

            if (progressed == 0 || candidates.Count < Batch)
            {
                break;
            }
        }

        return submitted;
    }
}
