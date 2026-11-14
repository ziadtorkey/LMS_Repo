using Lms.Domain.Calendar;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Calendar;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lms.Infrastructure.Timetable;

public static class TimetableServiceCollectionExtensions
{
    public static IServiceCollection AddTimetable(this IServiceCollection services)
    {
        services.AddScoped<IClientCalendar, ClientCalendar>();
        services.AddScoped<PeriodService>();
        services.TryAddScoped<IPeriodUsage, NoPeriodUsage>(); // Task 14 registers the slot-backed check before this one
        return services;
    }

    /// <summary>No slot table exists yet (Task 14): no period is in use.</summary>
    private sealed class NoPeriodUsage : IPeriodUsage
    {
        public Task<bool> IsUsedAsync(Guid periodId, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
