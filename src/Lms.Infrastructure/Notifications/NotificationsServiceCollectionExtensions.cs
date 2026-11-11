using Lms.Domain.Abstractions;
using Lms.Domain.Notifications;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Notifications;

public static class NotificationsServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationCore(this IServiceCollection services)
    {
        services.AddScoped<INotificationRecipientResolver, NotificationRecipientResolver>();
        services.AddScoped<INotificationPublisher>(sp => new NotificationPublisher(
            () => new LmsDbContext(
                sp.GetRequiredService<DbContextOptions<LmsDbContext>>(), sp.GetRequiredService<ICurrentContext>()),
            sp.GetRequiredService<ICurrentContext>(),
            sp.GetRequiredService<INotificationRecipientResolver>(),
            sp.GetRequiredService<INotificationPusher>(),
            sp.GetRequiredService<IClock>()));
        return services;
    }
}
