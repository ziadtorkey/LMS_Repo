using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Timetable;

/// <summary>Timetable: periods, working days, slots and exceptions (FR-TT-01..05). Tasks 13 and 14: periods, slots, conflicts and publish.</summary>
public sealed class TimetableModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        PeriodEndpoints.Map(app);
        app.MapTimetable();
        app.MapTimetableCopyExport();
    }
}
