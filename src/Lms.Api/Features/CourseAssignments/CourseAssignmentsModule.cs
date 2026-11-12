using Lms.Api.Infrastructure;
using Lms.Domain.Content;
using Lms.Infrastructure.Assignments;
using Lms.Infrastructure.Jobs;
using Lms.Shared;
using Lms.Shared.Assignments;
using Lms.Shared.Authorization;

namespace Lms.Api.Features.CourseAssignments;

/// <summary>Assignments of a class course and student submissions (M4a Task 8: FR-ASG-01, FR-ASG-02 submission part, D48, D51).</summary>
public sealed class CourseAssignmentsModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AssignmentHandlers>();
        services.AddScoped<SubmissionHandlers>();
        services.AddScoped<GradingHandlers>();
        services.AddScoped<AssignmentDueSoonJob>();
        services.AddSingleton(new DevJobDefinition(
            AssignmentDueSoonJob.RecurringJobId, (sp, asOf, ct) => sp.GetRequiredService<AssignmentDueSoonJob>().RunAsync(asOf, ct)));
        if (configuration.GetValue("Jobs:ServerEnabled", true))
        {
            services.AddHostedService<DueSoonRegistration>();
        }

        services.AddScoped<CourseAssignmentNotifier>();
        services.AddScoped<IAssetReferenceReader, AssignmentAssetReader>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.ClassCourseAssignmentsPattern,
                (Guid id, int? page, int? pageSize, HttpContext http, AssignmentHandlers h, CancellationToken ct) =>
                    h.ListAsync(id, page, pageSize, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.CourseAssignments.View);
        app.MapPost(Routes.ClassCourseAssignmentsPattern,
                (Guid id, CreateCourseAssignmentRequest? r, HttpContext http, AssignmentHandlers h, CancellationToken ct) =>
                    h.CreateAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.CourseAssignments.Manage);
        app.MapGet(Routes.CourseAssignmentPattern, (Guid id, HttpContext http, AssignmentHandlers h, CancellationToken ct) => h.GetAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.CourseAssignments.View);
        app.MapPut(Routes.CourseAssignmentPattern,
                (Guid id, UpdateCourseAssignmentRequest? r, HttpContext http, AssignmentHandlers h, CancellationToken ct) =>
                    h.UpdateAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.CourseAssignments.Manage);
        app.MapDelete(Routes.CourseAssignmentPattern, (Guid id, HttpContext http, AssignmentHandlers h, CancellationToken ct) => h.DeleteAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.CourseAssignments.Manage);
        app.MapGet(Routes.CourseAssignmentSubmissionsPattern,
                (Guid id, [AsParameters] SubmissionQuery query, HttpContext http, SubmissionHandlers h, CancellationToken ct) =>
                    h.ListAsync(id, query, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.CourseAssignments.Grade);
        app.MapGet(Routes.CourseAssignmentMySubmissionPattern,
                (Guid id, HttpContext http, SubmissionHandlers h, CancellationToken ct) => h.MyAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.CourseAssignments.Submit);
        app.MapPost(Routes.CourseAssignmentSubmitPattern,
                (Guid id, SubmitAssignmentRequest? r, HttpContext http, SubmissionHandlers h, CancellationToken ct) =>
                    h.SubmitAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.CourseAssignments.Submit);
        app.MapPut(Routes.SubmissionGradePattern,
                (Guid id, GradeSubmissionRequest? r, HttpContext http, GradingHandlers h, CancellationToken ct) => h.GradeAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.CourseAssignments.Grade);
        app.MapPost(Routes.SubmissionReturnPattern,
                (Guid id, HttpContext http, GradingHandlers h, CancellationToken ct) => h.ReturnAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.CourseAssignments.Grade);
        app.MapGet(Routes.SubmissionPattern, (Guid id, HttpContext http, SubmissionHandlers h, CancellationToken ct) => h.GetAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.CourseAssignments.View);
    }
}

internal sealed class DueSoonRegistration(IServiceScopeFactory scopes, ILogger<DueSoonRegistration> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<IBackgroundJobs>().AddOrUpdateRecurring<AssignmentDueSoonJob>(
                AssignmentDueSoonJob.RecurringJobId, job => job.RunAsync(null, CancellationToken.None), AssignmentDueSoonJob.Cron);
        }
        catch (Exception ex)
        {
            // Storage down at startup must not stop the API; the job is registered again on the next start.
            logger.LogError(ex, "The assignment-due-soon recurring job could not be registered");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
