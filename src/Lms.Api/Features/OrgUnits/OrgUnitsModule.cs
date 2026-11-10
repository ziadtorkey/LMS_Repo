namespace Lms.Api.Features.OrgUnits;

public static class OrgUnitsModule
{
    public static IServiceCollection AddOrgUnits(this IServiceCollection services)
    {
        services.AddScoped<OrgUnitQueries>();
        services.AddScoped<DeleteOrgUnitHandler>();
        services.AddScoped<AcademicFlagsHandler>();
        return services;
    }
}