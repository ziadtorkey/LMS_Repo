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
        services.AddScoped<TimetableService>();
        services.AddScoped<ExceptionService>();
        services.AddScoped<Lms.Infrastructure.Access.SubstituteRelationship>();
        services.AddScoped<IPeriodUsage, SlotPeriodUsage>();
        services.TryAddScoped<IAttendanceGenerationQueue, NoAttendanceGeneration>(); // Task 16 registers the real queue
        return services;
    }

    /// <summary>A period is in use when a live slot of the client takes it.</summary>
    private sealed class SlotPeriodUsage(Lms.Infrastructure.Persistence.LmsDbContext db) : IPeriodUsage
    {
        public Task<bool> IsUsedAsync(Guid periodId, CancellationToken cancellationToken) =>
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.Set<TimetableSlot>(), s => s.PeriodId == periodId, cancellationToken);
    }

    /// <summary>Until Task 16 nothing generates attendance sessions.</summary>
    private sealed class NoAttendanceGeneration : IAttendanceGenerationQueue
    {
        public Task<bool> QueueAsync(Guid organizationId, Guid classId, Guid termId, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
