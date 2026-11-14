using Lms.Domain.Abstractions;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.AcademicYears;
using Lms.Infrastructure.Timetable;
using Lms.Infrastructure.Auditing;
using Lms.Infrastructure.Packages;
using Lms.Infrastructure.Content;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Files;
using Lms.Infrastructure.Email;
using Lms.Infrastructure.Identity;
using Lms.Infrastructure.Identity.External;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Organizations;
using Lms.Infrastructure.Notifications;
using Lms.Infrastructure.Courses;
using Lms.Infrastructure.People;
using Lms.Infrastructure.Persistence;
using Lms.Infrastructure.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Single entry point; each module adds one line here that calls its own Add&lt;Module&gt; extension.</summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, Microsoft.Extensions.Hosting.IHostEnvironment environment)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddPersistence(configuration);
        services.AddAccess();
        services.AddOrganizations();
        services.AddDevSeeding();
        services.AddAuditing();
        services.AddNotificationCore();
        services.AddFileStorage(configuration);
        services.AddLmsIdentityStores();
        services.AddEmail(configuration);
        services.AddSettingsAndPolicies();
        services.AddAcademicYears();
        services.AddTimetable();
        services.AddPeople();
        services.AddEnrollment();
        services.AddScoped<Lms.Domain.Grading.IGradeEvents, Lms.Infrastructure.Grading.GradeEventWriter>();
        services.AddAcademic();
        services.AddContent();
        services.AddPackages(configuration);
        services.AddExternalIdentity(configuration, environment);
        return services;
    }
}
