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
        services.AddScoped<GroupService>();
        services.AddScoped<MessagingEvents>();
        services.AddScoped<GroupPostingRules>();
        services.AddScoped<MessageSearch>();
        services.AddSingleton<MessageSearchThrottle>();
        services.TryAddSingleton<IMessagingRealtime, NoOpMessagingRealtime>(); // the API registers the SignalR transport
        services.AddScoped<MessageAttachmentGuard>();
        services.TryAddSingleton<Lms.Domain.Files.IFileScanner, Lms.Domain.Files.NoOpFileScanner>(); // D65: off in v1
        return services;
    }
}
