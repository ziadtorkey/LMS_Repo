using Lms.Domain.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lms.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddMessaging(this IServiceCollection services)
    {
        services.AddScoped<ConversationAccess>();
        services.AddScoped<ConversationService>();
        services.AddScoped<MessageService>();
        services.AddScoped<ReadStateService>();
        services.TryAddScoped<IMessagingPolicy, AllowAllMessagingPolicy>(); // Task 11 registers the D69 policy
        return services;
    }
}
