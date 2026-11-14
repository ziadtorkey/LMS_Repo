using Lms.Api.Infrastructure;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Messaging;
using Lms.Infrastructure.Moderation;

namespace Lms.Api.Features.Messaging;

/// <summary>Registers the daily messaging retention jobs (M6 Task 16, D66, D71, L23) at startup; one id is one schedule. Each runs per client in the system context.</summary>
public sealed class MessagingRetentionModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(new DevJobDefinition(MessageRetentionJob.RecurringJobId, (sp, asOf, ct) => sp.GetRequiredService<MessageRetentionJob>().RunAsync(asOf, ct)));
        services.AddSingleton(new DevJobDefinition(RemovedOriginalRetentionJob.RecurringJobId, (sp, asOf, ct) => sp.GetRequiredService<RemovedOriginalRetentionJob>().RunAsync(asOf, ct)));
        services.AddSingleton(new DevJobDefinition(ReportContextRetentionJob.RecurringJobId, (sp, asOf, ct) => sp.GetRequiredService<ReportContextRetentionJob>().RunAsync(asOf, ct)));
        if (configuration.GetValue("Jobs:ServerEnabled", true))
        {
            services.AddHostedService<MessagingRetentionRegistration>();
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
    }
}

internal sealed class MessagingRetentionRegistration(IServiceScopeFactory scopes, ILogger<MessagingRetentionRegistration> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobs>();
            jobs.AddOrUpdateRecurring<MessageRetentionJob>(MessageRetentionJob.RecurringJobId, job => job.RunAsync(null, CancellationToken.None), MessageRetentionJob.Cron);
            jobs.AddOrUpdateRecurring<RemovedOriginalRetentionJob>(RemovedOriginalRetentionJob.RecurringJobId, job => job.RunAsync(null, CancellationToken.None), RemovedOriginalRetentionJob.Cron);
            jobs.AddOrUpdateRecurring<ReportContextRetentionJob>(ReportContextRetentionJob.RecurringJobId, job => job.RunAsync(null, CancellationToken.None), ReportContextRetentionJob.Cron);
        }
        catch (Exception ex)
        {
            // Storage down at startup must not stop the API; the jobs are registered again on the next start.
            logger.LogError(ex, "The messaging retention jobs could not be registered");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
