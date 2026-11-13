using Lms.Domain.Abstractions;
using Lms.Domain.Content;
using Lms.Domain.Grading;
using Lms.Domain.Packages;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Packages.Attempts;

/// <summary>How a commit or a finish ended (the endpoint maps it to a status code).</summary>
public enum AttemptOutcome
{
    Applied,

    /// <summary>A commit with a lower or equal <c>seq</c>: parallel requests arrive out of order, the highest wins; not an error (204).</summary>
    Ignored,

    /// <summary>The attempt is already closed (409 <c>package_commit_rejected</c>).</summary>
    Closed,

    /// <summary>Faster than <c>Packages.CommitMinIntervalSeconds</c> (429).</summary>
    RateLimited,

    /// <summary>The data model would pass its 64 KB cap (400).</summary>
    TooLarge,

    /// <summary>Concurrent writers kept winning: the caller may retry (409).</summary>
    Busy,
}

/// <summary>
/// Attempts of a package (M4b Task 9b): opens or resumes one, applies commits (highest <c>seq</c> wins, size and rate enforced by the
/// server), judges the data model through the format adapter and closes the attempt at <c>finish</c>, then keeps the gradebook seam
/// in line with the module's grading method (D57). Callers have checked permission, relationship, visibility and the feature flag.
/// </summary>
public sealed class PackageAttemptService(
    LmsDbContext db, IEnumerable<IPackageTracker> trackers, IGradeEvents grades, IClock clock)
{
    private const int Retries = 4;
    private const int BarrierTries = 10;
    private static readonly TimeSpan BarrierWait = TimeSpan.FromMilliseconds(200);

    public IPackageTracker TrackerFor(string kind) =>
        trackers.LastOrDefault(t => t.Kind == kind) ?? trackers.Last(t => t.Kind == "*");

    /// <summary>The student's open attempt of the package, or a new one (a completed package opened again is a new attempt, D57).</summary>
    public async Task<(PackageAttempt Attempt, bool Resumed)> OpenAsync(PackageUpload package, LessonModule module, Guid studentId, CancellationToken ct)
    {
        var open = await db.Set<PackageAttempt>().FirstOrDefaultAsync(a => a.PackageId == package.Id && a.StudentId == studentId && a.OpenMarker == 1, ct);
        if (open is not null)
        {
            return (open, true);
        }

        for (var i = 0; ; i++)
        {
            var attempt = new PackageAttempt(package, module.Id, studentId, CommitGate.FormatOf(package.Kind), clock.UtcNow);
            db.Add(attempt);
            try
            {
                await db.SaveChangesAsync(ct);
                return (attempt, false);
            }
            catch (DbUpdateException) when (i < Retries)
            {
                // A concurrent launch opened it first (a duplicate key, or MySQL picked this insert as the deadlock victim): continue that one.
                db.Entry(attempt).State = EntityState.Detached;
                var winner = await db.Set<PackageAttempt>().FirstOrDefaultAsync(a => a.PackageId == package.Id && a.StudentId == studentId && a.OpenMarker == 1, ct);
                if (winner is not null)
                {
                    return (winner, true);
                }
            }
        }
    }

    public async Task<AttemptOutcome> CommitAsync(
        Guid attemptId, PackageUpload package, LessonModule module, PackageCommitRequest request, int minIntervalSeconds, CancellationToken ct)
    {
        for (var i = 0; i < Retries; i++)
        {
            var attempt = await db.Set<PackageAttempt>().FirstAsync(a => a.Id == attemptId, ct);
            if (!attempt.IsOpen)
            {
                return AttemptOutcome.Closed;
            }

            if (request.Seq <= attempt.LastSeq)
            {
                return AttemptOutcome.Ignored;
            }

            var now = clock.UtcNow;
            if (attempt.LastCommitAtUtc is { } last && now - last < TimeSpan.FromSeconds(minIntervalSeconds))
            {
                return AttemptOutcome.RateLimited;
            }

            var merged = CommitGate.Merge(attempt.Model, request.Data);
            if (merged is null)
            {
                return AttemptOutcome.TooLarge;
            }

            attempt.ApplyCommit(request.Seq, merged, now);
            var judged = TrackerFor(package.Kind).Judge(package, module.CompletionRule, module.CompletionMinScore, attempt.Model);
            attempt.SetScore(judged.ScoreRaw, judged.ScoreMin, judged.ScoreMax);
            try
            {
                await db.SaveChangesAsync(ct);
                return AttemptOutcome.Applied;
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear(); // another commit of this attempt won the row: read it again and re-decide
            }
        }

        return AttemptOutcome.Busy;
    }

    /// <summary>
    /// Flushes and judges the attempt. <paramref name="lastSeq"/> is a barrier: the commits up to it were sent before this call and may
    /// still be in flight, so the server waits briefly for them. A content that met the completion rule closes the attempt
    /// (<c>Completed</c>); one that did not stays open and resumable.
    /// </summary>
    public async Task<(AttemptOutcome Outcome, PackageAttempt? Attempt)> FinishAsync(
        Guid attemptId, PackageUpload package, LessonModule module, long lastSeq, CancellationToken ct)
    {
        for (var i = 0; i < BarrierTries; i++)
        {
            var seen = await db.Set<PackageAttempt>().AsNoTracking().Where(a => a.Id == attemptId).Select(a => a.LastSeq).FirstAsync(ct);
            if (seen >= lastSeq)
            {
                break;
            }

            await Task.Delay(BarrierWait, ct);
        }

        for (var i = 0; i < Retries; i++)
        {
            var attempt = await db.Set<PackageAttempt>().FirstAsync(a => a.Id == attemptId, ct);
            if (!attempt.IsOpen)
            {
                return (AttemptOutcome.Closed, attempt);
            }

            var judged = TrackerFor(package.Kind).Judge(package, module.CompletionRule, module.CompletionMinScore, attempt.Model);
            attempt.SetScore(judged.ScoreRaw, judged.ScoreMin, judged.ScoreMax);
            if (judged.Completed)
            {
                attempt.Complete(clock.UtcNow);
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return (AttemptOutcome.Applied, attempt);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
            }
        }

        return (AttemptOutcome.Busy, null);
    }

    /// <summary>The attempt the gradebook holds for the student under the package's grading method (null when none is graded).</summary>
    public async Task<GradedAttempt?> GradedAsync(PackageUpload package, Guid studentId, CancellationToken ct) =>
        PackageGrading.Pick(package.GradingMethod ?? PackageUpload.HighestMethod, await CompletedAsync(package.Id, studentId, ct));

    /// <summary>
    /// Brings the grade event in line with the method (D57): the score the method picks goes to the gradebook seam, which writes a new
    /// revision only when it differs from the latest one. Returns the graded score.
    /// </summary>
    public async Task<decimal?> GradeAsync(PackageUpload package, Guid studentId, CancellationToken ct)
    {
        var picked = await GradedAsync(package, studentId, ct);
        if (picked?.ScoreRaw is not { } raw)
        {
            return null;
        }

        var kind = package.Kind == PackageKinds.H5p ? GradeSourceKind.H5p : GradeSourceKind.Scorm;
        await grades.RecordScoreAsync(
            kind, package.Id, package.ClassCourseId, studentId, raw, picked.ScoreMax is > 0m ? picked.ScoreMax.Value : PackageGrading.DefaultMax,
            clock.UtcNow, ct);
        await db.SaveChangesAsync(ct);
        return raw;
    }

    /// <summary>A change of the grading method: every student with a completed attempt is re-graded.</summary>
    public async Task RegradeAllAsync(PackageUpload package, CancellationToken ct)
    {
        var students = await db.Set<PackageAttempt>().Where(a => a.PackageId == package.Id && a.Status == PackageAttempt.CompletedStatus)
            .Select(a => a.StudentId).Distinct().ToListAsync(ct);
        foreach (var student in students)
        {
            await GradeAsync(package, student, ct);
        }
    }

    private async Task<List<GradedAttempt>> CompletedAsync(Guid packageId, Guid studentId, CancellationToken ct) =>
        (await db.Set<PackageAttempt>()
            .Where(a => a.PackageId == packageId && a.StudentId == studentId && a.Status == PackageAttempt.CompletedStatus && a.CompletedAtUtc != null)
            .Select(a => new { a.Id, a.CompletedAtUtc, a.ScoreRaw, a.ScoreMax }).ToListAsync(ct))
        .Select(a => new GradedAttempt(a.Id, a.CompletedAtUtc!.Value, a.ScoreRaw, a.ScoreMax)).ToList();
}
