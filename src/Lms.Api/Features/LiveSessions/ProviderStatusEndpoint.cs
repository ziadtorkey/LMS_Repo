using Lms.Api.Infrastructure;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Zoom;
using Lms.Shared;
using Lms.Shared.Authorization;

namespace Lms.Api.Features.LiveSessions;

/// <summary>
/// Zoom provider wiring and the provider-status endpoint (M4b Task 5). The status says only whether the platform Zoom app is
/// configured and healthy; it never reveals account ids, hosts or secrets.
/// </summary>
public sealed class ZoomProviderModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddZoom(configuration);
        services.AddSingleton(new DevJobDefinition(
            ZoomSyncSweepJob.RecurringJobId, (sp, _, ct) => sp.GetRequiredService<ZoomSyncSweepJob>().RunAsync(ct)));
        if (configuration.GetValue("Jobs:ServerEnabled", true))
        {
            services.AddHostedService<ZoomSyncSweepRegistration>();
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet(Routes.LiveSessionsProviderStatus, async (ZoomProviderStatus status, CancellationToken ct) =>
                Results.Ok(await status.GetAsync(ct)))
            .RequireAuthorization()
            .RequirePermission(Permissions.LiveSessions.Manage);
}

/// <summary>Registers the recurring <c>zoom-sync-sweep</c> job at startup; one id is one schedule.</summary>
internal sealed class ZoomSyncSweepRegistration(IServiceScopeFactory scopes, ILogger<ZoomSyncSweepRegistration> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<IBackgroundJobs>().AddOrUpdateRecurring<ZoomSyncSweepJob>(
                ZoomSyncSweepJob.RecurringJobId, job => job.RunAsync(CancellationToken.None), ZoomSyncSweepJob.Cron);
        }
        catch (Exception ex)
        {
            // Storage down at startup must not stop the API; the job is registered again on the next start.
            logger.LogError(ex, "The zoom-sync-sweep recurring job could not be registered");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
