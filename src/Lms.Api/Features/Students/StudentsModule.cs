using Lms.Api.Features.Students.SelfService;
using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Students;

/// <summary>Student profiles (FR-STU-01..03): create with code, guardians, account and placement; list, profile, edit, status, delete, flags, reset.</summary>
public sealed class StudentsModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<StudentMapper>();
        services.AddScoped<StudentQueries>();
        services.AddScoped<CreateStudentHandler>();
        services.AddScoped<StudentLifecycleHandler>();
        services.AddScoped<StudentChangeApplier>();
        services.AddScoped<ChangeRequestMapper>();
        services.AddScoped<ChangeRequestNotifier>();
        services.AddScoped<MyStudentProfileHandler>();
        services.AddScoped<MyChangeRequestsHandler>();
        services.AddScoped<ChangeRequestDecisionHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        StudentEndpoints.Map(app);
        SelfServiceEndpoints.Map(app);
    }
}
