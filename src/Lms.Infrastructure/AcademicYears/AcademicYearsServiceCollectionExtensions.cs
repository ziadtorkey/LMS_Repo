using Lms.Domain.AcademicYears;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.AcademicYears;

public static class AcademicYearsServiceCollectionExtensions
{
    public static IServiceCollection AddAcademicYears(this IServiceCollection services)
    {
        services.AddScoped<IAcademicYearResolver, AcademicYearResolver>();
        services.AddScoped<IAcademicCalendar, AcademicCalendar>();
        return services;
    }
}
