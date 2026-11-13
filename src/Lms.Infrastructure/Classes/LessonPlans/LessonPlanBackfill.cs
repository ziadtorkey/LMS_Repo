using Lms.Domain.Classes;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Classes.LessonPlans;

/// <summary>
/// Creates the plan of every class course that has none (class courses made before this module existed, or while it was
/// failing), copied from the course's latest published blueprint. Idempotent; run at start with a platform context.
/// </summary>
public sealed class LessonPlanBackfill(LmsDbContext db, ClassLessonPlanSync sync, ILogger<LessonPlanBackfill> logger)
{
    private const int BatchSize = 200;

    /// <returns>The number of plans created.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var created = 0;
        while (true)
        {
            var batch = await db.Set<ClassCourse>()
                .Where(cc => !db.Set<ClassLessonPlan>().Any(p => p.ClassCourseId == cc.Id))
                .OrderBy(cc => cc.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var classCourse in batch)
            {
                await sync.CreatePlanAsync(classCourse, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            created += batch.Count;
        }

        if (created > 0)
        {
            logger.LogInformation("Lesson plan backfill created {Count} plans.", created);
        }

        return created;
    }
}

public static class LessonPlansServiceCollectionExtensions
{
    /// <summary>Class lesson plans (M3 Task 17): copy on create, push on publish, backfill.</summary>
    public static IServiceCollection AddLessonPlans(this IServiceCollection services)
    {
        services.AddScoped<LessonPlanReads>();
        services.AddScoped<ClassLessonPlanSync>();
        services.AddScoped<Lms.Domain.Classes.IClassCourseLifecycleHandler>(sp => sp.GetRequiredService<ClassLessonPlanSync>());
        services.AddScoped<Lms.Domain.Blueprints.IBlueprintPublished>(sp => sp.GetRequiredService<ClassLessonPlanSync>());
        services.AddScoped<BlueprintPushJob>();
        services.AddScoped<LessonPlanBackfill>();
        return services;
    }
}
