namespace Lms.Api.Features.AcademicYears;

public static class AcademicYearsModule
{
    public static IEndpointRouteBuilder MapAcademicYears(this IEndpointRouteBuilder app)
    {
        app.MapAcademicYearEndpoints();
        app.MapTermEndpoints();
        app.MapCalendarEndpoints();
        return app;
    }
}
