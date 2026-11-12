using Lms.Api.Infrastructure;
using Lms.Infrastructure.Classes.LessonPlans;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Classes;

/// <summary>Class lesson plans (M3 Task 17): the plan tree endpoint and the start-up backfill of missing plans.</summary>
public sealed class LessonPlansModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLessonPlans();
        services.AddHostedService<LessonPlanBackfillStartup>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => LessonPlanEndpoints.Map(app);
}

internal sealed class LessonPlanBackfillStartup(IServiceScopeFactory scopes, ILogger<LessonPlanBackfillStartup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true);
            var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();

            // Never run against a schema that is behind (migrations are a deployment step outside Development).
            if ((await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                logger.LogWarning("Skipping lesson plan backfill: database has pending migrations.");
                return;
            }

            await scope.ServiceProvider.GetRequiredService<LessonPlanBackfill>().RunAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed backfill must not stop the API; it is retried on the next start.
            logger.LogError(ex, "Lesson plan backfill failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
