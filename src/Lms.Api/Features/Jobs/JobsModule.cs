using System.Text.Json;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.MySql;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Persistence;
using Hangfire.Server;

namespace Lms.Api.Features.Jobs;

/// <summary>
/// Background jobs (Hangfire on MySQL). Jobs run in their own DI scope with the context of whoever enqueued them
/// (<see cref="IBackgroundJobs"/>), so the global query filters still apply. The dashboard exists only in Development,
/// behind the platform-admin check. Set <c>Jobs:ServerEnabled=false</c> to run the API without a job server.
/// </summary>
public sealed class JobsModule : ILmsModule
{
    public const string DashboardPath = "/hangfire";

    /// <summary>Development-only manual trigger: <c>POST /api/dev/jobs/{jobId}/run?asOf=yyyy-MM-dd</c>.</summary>
    public const string DevRunPattern = "/api/dev/jobs/{jobId}/run";

    private static async Task<IResult> RunDevJobAsync(
        string jobId, string? asOf, IEnumerable<DevJobDefinition> jobs, IServiceScopeFactory scopes, CancellationToken ct)
    {
        var job = jobs.FirstOrDefault(j => string.Equals(j.Id, jobId, StringComparison.Ordinal));
        if (job is null)
        {
            return ApiProblems.NotFound();
        }

        DateOnly? day = null;
        if (!string.IsNullOrEmpty(asOf))
        {
            if (!DateOnly.TryParseExact(asOf, "yyyy-MM-dd", out var parsed))
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { [nameof(asOf)] = ["date_invalid"] },
                    extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });
            }

            day = parsed;
        }

        // A fresh scope with the platform context, exactly like a scheduled system job.
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true);
        await job.Run(scope.ServiceProvider, day, ct);
        return Results.Ok(new { jobId, asOf = day?.ToString("yyyy-MM-dd") });
    }

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Lms")
            ?? throw new InvalidOperationException("ConnectionStrings:Lms is not configured.");
        var pollSeconds = int.TryParse(configuration["Jobs:PollIntervalSeconds"], out var p) && p > 0 ? p : 5;

        services.AddHangfire((provider, config) => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseActivator(new LmsJobActivator(provider.GetRequiredService<IServiceScopeFactory>()))
            .UseFilter(new CaptureJobContextFilter())
            .UseStorage(new MySqlStorage(connectionString, new MySqlStorageOptions
            {
                TablesPrefix = "Hangfire",
                PrepareSchemaIfNecessary = true,
                QueuePollInterval = TimeSpan.FromSeconds(pollSeconds),
            })));

        if (configuration.GetValue("Jobs:ServerEnabled", true))
        {
            services.AddHangfireServer(options =>
            {
                options.WorkerCount = Math.Max(1, configuration.GetValue("Jobs:WorkerCount", 2));
                options.Queues = JobQueues.All;
            });
        }

        services.AddSingleton(JobSettings.Parse(configuration[JobSettings.TimeZoneKey]));
        services.AddScoped<IBackgroundJobs, BackgroundJobs>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        if (!app.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            return;
        }

        app.MapHangfireDashboard(DashboardPath, new DashboardOptions
        {
            DashboardTitle = "LMS jobs",
            Authorization = [new PlatformAdminDashboardFilter()],
            IsReadOnlyFunc = _ => false,
        });

        // Development only (the route does not exist elsewhere): run a job now, as the scheduler would, and wait for it.
        // Platform admins only (Clients.Update is a platform-only permission). Not part of Lms.Shared on purpose.
        app.MapPost(DevRunPattern, RunDevJobAsync).RequireAuthorization().RequirePermission(Permissions.Clients.Update);
    }
}

/// <summary>Only an authenticated platform admin may open the dashboard; nobody else, never anonymously.</summary>
public sealed class PlatformAdminDashboardFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var user = context.GetHttpContext().User;
        return user.Identity?.IsAuthenticated == true && user.HasClaim("platform_admin", "true");
    }
}

/// <summary>Runs every job in its own DI scope and restores the context captured when it was enqueued.</summary>
public sealed class LmsJobActivator(IServiceScopeFactory scopes) : JobActivator
{
    public override JobActivatorScope BeginScope(JobActivatorContext context)
    {
        var scope = scopes.CreateScope();
        var current = scope.ServiceProvider.GetRequiredService<HttpCurrentContext>();
        if (context.GetJobParameter<string>(JobContextSnapshot.ParameterName) is { Length: > 0 } json
            && JsonSerializer.Deserialize<JobContextSnapshot>(json) is { } snapshot)
        {
            Restore(scope.ServiceProvider, current, snapshot);
        }

        // No captured context: the scope keeps an empty context, so scoped queries return nothing.
        return new ActivatorScope(scope);
    }

    private static void Restore(IServiceProvider services, HttpCurrentContext current, JobContextSnapshot snapshot)
    {
        if (snapshot.IsPlatformAdmin)
        {
            current.SetUser(snapshot.UserId ?? Guid.Empty, true);
            return;
        }

        if (snapshot.OrganizationId is null)
        {
            current.SetUser(snapshot.UserId ?? Guid.Empty, false);
            return;
        }

        // A job may run long after it was enqueued (queued, retried). The assignment it was enqueued under must still be
        // valid now (not deleted, expired or in an inactive unit, D15); otherwise the job runs with an empty context.
        // The scope comes from the live assignment, not from the snapshot, so a moved unit is honoured too.
        var db = services.GetRequiredService<LmsDbContext>();
        var today = DateOnly.FromDateTime(services.GetRequiredService<IClock>().UtcNow);
        var valid = snapshot is { UserId: { } userId, AssignmentId: { } assignmentId }
            ? db.ValidAssignmentsFor(userId, today).Where(a => a.AssignmentId == assignmentId).FirstOrDefault()
            : null;
        if (valid is null || valid.OrganizationId != snapshot.OrganizationId)
        {
            return;
        }

        current.SetUser(valid.UserId, false);
        current.SetAssignment(valid.AssignmentId, valid.OrganizationId, valid.OrgUnitPath);
    }

    private sealed class ActivatorScope(IServiceScope scope) : JobActivatorScope
    {
        public override object Resolve(Type type) => ActivatorUtilities.GetServiceOrCreateInstance(scope.ServiceProvider, type);

        public override void DisposeScope() => scope.Dispose();
    }
}
