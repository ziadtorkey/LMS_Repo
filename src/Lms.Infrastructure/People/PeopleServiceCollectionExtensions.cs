using Lms.Domain.Organizations;
using Lms.Domain.People;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.People;

public static class PeopleServiceCollectionExtensions
{
    public static IServiceCollection AddPeople(this IServiceCollection services)
    {
        services.AddScoped<IPersonDuplicateFinder, PersonDuplicateFinder>();
        services.AddScoped<IOrgUnitUsageCheck, PeopleUsageCheck>();
        return services;
    }
}
