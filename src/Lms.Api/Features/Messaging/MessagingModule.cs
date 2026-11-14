using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Messaging;

/// <summary>Messaging (FR-MSG-01, M6 Task 10): conversations, messages and unread state. Every route needs the <c>Messaging</c> flag and is participant-only (L6).</summary>
public sealed class MessagingModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ConversationMapper>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapConversations();
}
