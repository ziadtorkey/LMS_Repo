using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Content;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Content;

/// <summary>What happened to a module that may move a student completion.</summary>
public enum CompletionSignal
{
    Viewed,
    Marked,
    Submitted,
    Scored,
}

/// <summary>
/// The single writer of <see cref="ModuleCompletion"/> (FR-CNT-04): idempotent, never downgrades, safe under concurrent
/// requests. Assignments and quizzes call it with <see cref="CompletionSignal.Submitted"/> / <see cref="CompletionSignal.Scored"/>.
/// </summary>
public sealed class CompletionRecorder(LmsDbContext db, IClock clock)
{
    public async Task<ModuleCompletion> RecordAsync(
        LessonModule module, Guid studentId, CompletionSignal signal, decimal? score, CancellationToken ct)
    {
        var state = StateFor(module, signal, score);
        for (var attempt = 0; ; attempt++)
        {
            var row = await db.Set<ModuleCompletion>().FirstOrDefaultAsync(c => c.ModuleId == module.Id && c.StudentId == studentId, ct);
            var stored = signal == CompletionSignal.Scored ? score : null;
            if (row is null)
            {
                row = new ModuleCompletion(module, studentId, state, clock.UtcNow);
                if (stored is not null)
                {
                    row.MoveTo(state, clock.UtcNow, stored);
                }

                db.Add(row);
            }
            else
            {
                row.MoveTo(state, clock.UtcNow, stored);
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return row;
            }
            catch (DbUpdateException ex) when (attempt < 2 && DbErrors.IsConcurrentKeyConflict(ex))
            {
                // A concurrent request created the row first: read it and apply the signal on top.
                db.ChangeTracker.Clear();
            }
        }
    }

    private static string StateFor(LessonModule module, CompletionSignal signal, decimal? score) => signal switch
    {
        CompletionSignal.Viewed => module.CompletionRule == CompletionRules.View ? CompletionStates.Completed : CompletionStates.InProgress,
        CompletionSignal.Marked => module.CompletionRule == CompletionRules.Manual ? CompletionStates.Completed : CompletionStates.InProgress,
        CompletionSignal.Submitted => module.CompletionRule == CompletionRules.Submit ? CompletionStates.Completed : CompletionStates.InProgress,
        _ => module.CompletionRule == CompletionRules.Score && score is { } s && (module.CompletionMinScore is not { } min || s >= min)
            ? CompletionStates.Completed
            : CompletionStates.InProgress,
    };
}
