using Lms.Api.Infrastructure;
using Lms.Infrastructure.Moderation;

namespace Lms.Api.Features.Moderation;

/// <summary>Reports and moderation of messages (M6 Task 15, FR-MSG-04, D70).</summary>
public sealed class ModerationModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ReportService>();
        services.AddScoped<ModerationQueue>();
        services.AddScoped<ModerationActionsService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => ModerationEndpoints.Map(app);
}
