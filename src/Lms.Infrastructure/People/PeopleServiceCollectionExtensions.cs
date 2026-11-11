using Lms.Domain.Organizations;
using Lms.Domain.People;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.People;

public static class PeopleServiceCollectionExtensions
{
    public static IServiceCollection AddPeople(this IServiceCollection services)
    {
        services.AddScoped<IPersonDuplicateFinder, PersonDuplicateFinder>();
        services.AddScoped<IGuardianLinker, Guardians.GuardianLinker>();
        services.AddScoped<IStudentCredentials, Students.StudentCredentials>();
        services.AddScoped<IOrgUnitUsageCheck, PeopleUsageCheck>();
        services.AddScoped<IPersonAccounts, PersonAccounts>();
        services.AddScoped<IPersonStatusService, PersonStatusService>();
        services.AddScoped<IRoleAssignmentSync, RoleAssignmentSync>();
        services.AddScoped<IInvitationOutbox, InvitationOutbox>();
        services.AddScoped<IStudentCodeGenerator, StudentCodeGenerator>();
        return services;
    }
}
