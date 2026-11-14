using Lms.Api.Infrastructure;
using Lms.Domain.Messaging;
using Lms.Infrastructure.Messaging.Policy;

namespace Lms.Api.Features.Messaging;

/// <summary>The messaging safeguarding policy and its endpoints (M6 Task 11, FR-MSG-02, D69). Conversations, messages and search are the module of Task 10 and 12.</summary>
public sealed class MessagingPolicyModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<RelationshipLookup>();
        services.AddScoped<IMessagingPolicy, MessagingPolicy>();
        services.AddScoped<MessagingRulesService>();
        services.AddScoped<RecipientSearch>();
        services.AddScoped<RulesSummaryBuilder>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => MessagingPolicyEndpoints.Map(app);
}
