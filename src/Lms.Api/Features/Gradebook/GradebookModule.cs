using Lms.Api.Infrastructure;
using Lms.Infrastructure.Grading.Publishing;
using Lms.Infrastructure.Jobs;

namespace Lms.Api.Features.Gradebook;

/// <summary>The staff gradebook: grid, configuration, categories, items and entries (M5 Task 8). Services are registered by the infrastructure.</summary>
public sealed class GradebookModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(new DevJobDefinition(
            ScheduledPublishJob.RecurringJobId, (sp, _, ct) => sp.GetRequiredService<ScheduledPublishJob>().RunAsync(ct)));
        if (configuration.GetValue("Jobs:ServerEnabled", true))
        {
            services.AddHostedService<ScheduledPublishRegistration>();
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGradebookEndpoints();
        app.MapGradebookConfigEndpoints();
        app.MapGradeEntryEndpoints();
        app.MapPublishEndpoints();
    }
}

internal sealed class ScheduledPublishRegistration(IServiceScopeFactory scopes, ILogger<ScheduledPublishRegistration> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<IBackgroundJobs>().AddOrUpdateRecurring<ScheduledPublishJob>(
                ScheduledPublishJob.RecurringJobId, job => job.RunAsync(CancellationToken.None), ScheduledPublishJob.Cron);
        }
        catch (Exception ex)
        {
            // Storage down at startup must not stop the API; the job is registered again on the next start.
            logger.LogError(ex, "The scheduled grade publish recurring job could not be registered");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
