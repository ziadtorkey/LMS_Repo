using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Announcements;

/// <summary>Targeted announcements (M6 Task 13, FR-MSG-03): authoring, scope guard, drafts. Audience, publishing and the reader feed follow in the same task and Task 14.</summary>
public sealed class AnnouncementsModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<TargetScopeGuard>();
        services.AddScoped<AnnouncementMapper>();
        services.AddScoped<Lms.Infrastructure.Announcements.DeliverySummaryReader>();
        services.AddScoped<Lms.Infrastructure.Announcements.AudienceResolver>();
        services.AddScoped<Lms.Infrastructure.Announcements.AnnouncementService>();
        services.AddScoped<Lms.Infrastructure.Announcements.AnnouncementJobs>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => AnnouncementEndpoints.Map(app);
}
