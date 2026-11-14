using Lms.Api.Infrastructure;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Attendance;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lms.Api.Features.Attendance;

/// <summary>Attendance: session generation, ad hoc sessions and the day list (FR-ATT-01, FR-TT-06, M5 Task 16). Taking attendance, the live proposal and reports follow.</summary>
public sealed class AttendanceModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<AttendanceSessionGenerator>();
        services.AddScoped<AttendanceGenerationJob>();
        services.AddScoped<AttendanceRoster>();
        services.AddScoped<AttendanceListMapper>();
        services.AddScoped<AttendanceRecordsMapper>();
        services.AddScoped<AbsenceNotifier>();
        services.Replace(ServiceDescriptor.Scoped<IAttendanceGenerationQueue, AttendanceGenerationQueue>()); // replaces the no-op of Task 14
        if (configuration.GetValue("Jobs:ServerEnabled", true))
        {
            services.AddHostedService<AttendanceGenerationRegistration>();
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapAttendanceSessions();
        app.MapTakeAttendance();
        app.MapAttendanceReads();
    }
}
