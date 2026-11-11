using Lms.Api.Infrastructure;
using Lms.Infrastructure.People;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Staff;

/// <summary>Creates staff profiles for the bare users M1 left behind (M2 Task 9), at every start; idempotent.</summary>
public sealed class StaffBackfillModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<StaffBackfill>();
        services.AddHostedService<StaffBackfillStartup>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
    }
}

internal sealed class StaffBackfillStartup(IServiceScopeFactory scopes, ILogger<StaffBackfillStartup> logger) : IHostedService
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
                logger.LogWarning("Skipping staff backfill: database has pending migrations.");
                return;
            }

            await scope.ServiceProvider.GetRequiredService<StaffBackfill>().RunAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed backfill must not stop the API; it is retried on the next start.
            logger.LogError(ex, "Staff backfill failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
