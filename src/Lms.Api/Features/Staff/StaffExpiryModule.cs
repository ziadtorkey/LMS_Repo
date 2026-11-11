using Lms.Api.Infrastructure;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.People.Staff;

namespace Lms.Api.Features.Staff;

/// <summary>Registers the daily <c>staff-expiry</c> recurring job (M2 Task 19) at startup; one id is one schedule.</summary>
public sealed class StaffExpiryModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<StaffExpiryJob>();
        services.AddSingleton(new DevJobDefinition(
            StaffExpiryJob.RecurringJobId, (sp, asOf, ct) => sp.GetRequiredService<StaffExpiryJob>().RunAsync(asOf, ct)));
        if (configuration.GetValue("Jobs:ServerEnabled", true))
        {
            services.AddHostedService<StaffExpiryRegistration>();
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
    }
}

internal sealed class StaffExpiryRegistration(IServiceScopeFactory scopes, ILogger<StaffExpiryRegistration> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<IBackgroundJobs>().AddOrUpdateRecurring<StaffExpiryJob>(
                StaffExpiryJob.RecurringJobId, job => job.RunAsync(null, CancellationToken.None), StaffExpiryJob.Cron);
        }
        catch (Exception ex)
        {
            // Storage down at startup must not stop the API; the job is registered again on the next start.
            logger.LogError(ex, "The staff-expiry recurring job could not be registered");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
