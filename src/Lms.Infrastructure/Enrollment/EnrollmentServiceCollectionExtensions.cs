using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Enrollment;

public static class EnrollmentServiceCollectionExtensions
{
    /// <summary>The enrollment engine and its class-course lifecycle handler (M3 Task 24).</summary>
    public static IServiceCollection AddEnrollment(this IServiceCollection services)
    {
        services.AddScoped<PlacementGuard>();
        services.AddScoped<EnrollmentEngine>();
        services.AddScoped<IEnrollmentEngine>(sp => sp.GetRequiredService<EnrollmentEngine>());
        services.AddScoped<IClassCourseLifecycleHandler, EnrollmentClassCourseHandler>();
        services.AddScoped<IGroupTargets, GroupTargets>();
        return services;
    }
}
