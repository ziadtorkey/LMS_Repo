using Lms.Domain.Abstractions;
using Lms.Domain.Notifications;
using Lms.Infrastructure.Comms;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Notifications;

public static class NotificationsServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationCore(this IServiceCollection services)
    {
        services.AddScoped<INotificationRecipientResolver, NotificationRecipientResolver>();
        services.AddScoped<NotificationPreferences>();
        services.AddScoped<INotificationPreferences>(sp => sp.GetRequiredService<NotificationPreferences>());
        services.AddScoped<INotificationPublisher>(sp => new NotificationPublisher(
            () => new LmsDbContext(
                sp.GetRequiredService<DbContextOptions<LmsDbContext>>(), sp.GetRequiredService<ICurrentContext>()),
            sp.GetRequiredService<ICurrentContext>(),
            sp.GetRequiredService<INotificationRecipientResolver>(),
            sp.GetRequiredService<INotificationPusher>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<DeliveryPlanner>()));
        return services;
    }
}
