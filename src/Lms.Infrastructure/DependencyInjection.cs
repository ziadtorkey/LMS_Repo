using Lms.Domain.Abstractions;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.AcademicYears;
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
        services.AddPeople();
        services.AddEnrollment();
        services.AddScoped<Lms.Domain.Grading.IGradeEvents, Lms.Infrastructure.Grading.GradeEventWriter>();
        services.AddScoped<Lms.Infrastructure.Grading.Gradebooks.EventTermResolver>();
        services.AddScoped<Lms.Domain.Grading.IGradebookWriter, Lms.Infrastructure.Grading.Gradebooks.GradebookWriter>();
        services.AddScoped<Lms.Infrastructure.Grading.Gradebooks.GradebookItemsObserver>();
        services.AddScoped<Lms.Domain.Grading.IGradeEventObserver>(sp => sp.GetRequiredService<Lms.Infrastructure.Grading.Gradebooks.GradebookItemsObserver>());
        services.AddScoped<Lms.Infrastructure.Grading.Gradebooks.GradebookBackfill>();
        services.AddScoped<Lms.Infrastructure.Grading.Gradebooks.GradebookReader>();
        services.AddScoped<Lms.Infrastructure.Grading.Gradebooks.GradebookEditor>();
        services.AddScoped<Lms.Infrastructure.AcademicYears.ITermDependants, Lms.Infrastructure.Grading.Gradebooks.GradebookTermDependants>();
        Lms.Infrastructure.Grading.Scales.GradingScalesServiceCollectionExtensions.AddGradingScales(services);
        services.AddScoped<Lms.Domain.Grading.IScaleUsage, Lms.Infrastructure.Grading.Gradebooks.GradebookScaleUsage>(); // replaces the TryAdd default
        services.AddAcademic();
        services.AddContent();
        services.AddPackages(configuration);
        services.AddExternalIdentity(configuration, environment);
        return services;
    }
}
