using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Content;

/// <summary>
/// A student progress in one class course (FR-CNT-04), computed on read: completed over total of the modules that count
/// (rule not None, visible to that student now), per lesson and for the course, plus the "continue" module.
/// </summary>
public sealed class ProgressReader(LmsDbContext db, ModuleAccess access, IClock clock)
{
    public sealed record Snapshot(
        ProgressDto Course,
        IReadOnlyDictionary<Guid, ProgressDto> Lessons,
        IReadOnlyList<ModuleCompletionDto> Modules,
        IReadOnlyDictionary<Guid, string> States,
        Guid? NextModuleId);

    public async Task<Snapshot> ReadAsync(Guid classCourseId, Guid studentId, CancellationToken ct)
    {
        var group = await access.GroupOfAsync(classCourseId, studentId, ct);
        var visible = await access.VisibleTo(classCourseId, group, clock.UtcNow).ToListAsync(ct);
        var ids = visible.Select(p => p.Module.Id).ToList();
        var rows = await db.Set<ModuleCompletion>().Where(c => c.StudentId == studentId && ids.Contains(c.ModuleId)).ToListAsync(ct);
        var byModule = rows.ToDictionary(c => c.ModuleId);
        var counted = visible.Where(p => p.Module.CompletionRule != CompletionRules.None).Select(p => p.Module).ToList();

        bool Done(LessonModule m) => byModule.TryGetValue(m.Id, out var c) && c.State == CompletionStates.Completed;
        var lessons = counted.GroupBy(m => m.LessonKey)
            .ToDictionary(g => g.Key, g => new ProgressDto(g.Count(Done), g.Count()));
        var modules = counted.Select(m =>
        {
            var row = byModule.GetValueOrDefault(m.Id);
            return new ModuleCompletionDto(m.Id, m.CompletionRule, row?.State ?? CompletionStates.NotStarted, ModuleMapper.Utc(row?.CompletedAtUtc), row?.Score);
        }).ToList();
        var states = byModule.ToDictionary(kv => kv.Key, kv => kv.Value.State);
        return new Snapshot(
            new ProgressDto(counted.Count(Done), counted.Count), lessons, modules, states, counted.FirstOrDefault(m => !Done(m))?.Id);
    }

    /// <summary>The lessons of the plan in order (progress 0 / 0 for a lesson without counted modules).</summary>
    public async Task<List<LessonProgressDto>> LessonsAsync(Guid classCourseId, Snapshot snapshot, bool hideRemoved, CancellationToken ct)
    {
        var lessons = await (from p in db.Set<ClassLessonPlan>()
                             join s in db.Set<ClassSection>() on p.Id equals s.PlanId
                             join l in db.Set<ClassLesson>() on s.Id equals l.SectionId
                             where p.ClassCourseId == classCourseId && (!hideRemoved || (!s.IsHidden && !l.IsHidden))
                             orderby s.Order, l.Order
                             select l.Key).ToListAsync(ct);
        return lessons.Select(k => new LessonProgressDto(k, snapshot.Lessons.GetValueOrDefault(k, new ProgressDto(0, 0)))).ToList();
    }
}
