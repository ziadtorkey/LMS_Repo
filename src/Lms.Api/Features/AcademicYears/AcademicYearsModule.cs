using Lms.Api.Infrastructure;

namespace Lms.Api.Features.AcademicYears;

/// <summary>Academic years, terms and calendar endpoints (discovered automatically).</summary>
public sealed class AcademicYearsModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapAcademicYearEndpoints();
        app.MapTermEndpoints();
        app.MapCalendarEndpoints();
    }
}
