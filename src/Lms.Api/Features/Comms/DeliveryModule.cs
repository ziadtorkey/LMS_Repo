using Lms.Api.Infrastructure;
using Lms.Domain.Comms;
using Lms.Infrastructure.Comms;
using Lms.Infrastructure.Jobs;

namespace Lms.Api.Features.Comms;

/// <summary>
/// The delivery outbox kernel (M6 Task 5): planner, contact resolver, dispatcher and retention jobs, and their recurring schedules. A real channel
/// (the email relay, the push provider) registers its own <see cref="IDeliveryChannel"/>; the fake channels exist only where <c>Comms:FakeChannels</c> is
/// true (Development and the integration host), so nothing is "delivered" to nowhere in production.
/// </summary>
public sealed class DeliveryModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<CommsClientSettings>();
        services.AddScoped<IContactResolver, UserContactResolver>();
        services.AddScoped<DeliveryPlanner>();
        services.AddScoped<DeliveryDispatchJob>();
        services.AddScoped<DeliveryRetentionJob>();

        if (configuration.GetValue("Comms:FakeChannels", false))
        {
            services.AddSingleton<FakeDeliveryChannels>();
            services.AddSingleton<IDeliveryChannel>(sp => sp.GetRequiredService<FakeDeliveryChannels>().Email);
            services.AddSingleton<IDeliveryChannel>(sp => sp.GetRequiredService<FakeDeliveryChannels>().Push);
        }

        if (configuration.GetValue("Jobs:ServerEnabled", true))
        {
            services.AddHostedService<DeliveryJobsRegistration>();
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
    }
}

internal sealed class DeliveryJobsRegistration(IServiceScopeFactory scopes, ILogger<DeliveryJobsRegistration> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobs>();
            jobs.AddOrUpdateRecurring<DeliveryDispatchJob>(
                DeliveryDispatchJob.RecurringJobId, job => job.RunAsync(CancellationToken.None), DeliveryDispatchJob.Cron);
            jobs.AddOrUpdateRecurring<DeliveryRetentionJob>(
                DeliveryRetentionJob.RecurringJobId, job => job.RunAsync(CancellationToken.None), DeliveryRetentionJob.Cron);
        }
        catch (Exception ex)
        {
            // Storage down at startup must not stop the API; the jobs are registered again on the next start.
            logger.LogError(ex, "The delivery recurring jobs could not be registered");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
