using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Classes;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Courses;

public static class AcademicServiceCollectionExtensions
{
    /// <summary>Academic kernel services: course catalog and the usage checks that block deleting a unit.</summary>
    public static IServiceCollection AddAcademic(this IServiceCollection services)
    {
        services.AddScoped<ICourseCatalog, CourseCatalog>();
        services.AddScoped<IOrgUnitUsageCheck, CoursesUsageCheck>();
        services.AddScoped<IOrgUnitUsageCheck, ClassesUsageCheck>();
        return services;
    }
}
