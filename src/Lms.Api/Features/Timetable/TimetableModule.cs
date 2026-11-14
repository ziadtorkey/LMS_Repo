using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Timetable;

/// <summary>Timetable: periods, working days, slots and exceptions (FR-TT-01..05). Task 13 adds the periods.</summary>
public sealed class TimetableModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => PeriodEndpoints.Map(app);
}
