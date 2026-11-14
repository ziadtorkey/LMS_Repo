using Lms.Domain.AcademicYears;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.AcademicYears;

public static class AcademicYearsServiceCollectionExtensions
{
    public static IServiceCollection AddAcademicYears(this IServiceCollection services)
    {
        services.AddScoped<IAcademicYearResolver, AcademicYearResolver>();
        services.AddScoped<IAcademicCalendar, AcademicCalendar>();
        services.AddScoped<TermStructureService>();
        services.AddScoped<ITermDependants, NoTermDependants>(); // each module that hangs data on terms registers its own (M5 L8)
        return services;
    }
}
