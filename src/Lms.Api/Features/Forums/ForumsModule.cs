using Lms.Api.Infrastructure;
using Lms.Domain.Content;

namespace Lms.Api.Features.Forums;

/// <summary>Course forums and announcements (M4a Task 13, FR-FOR-01).</summary>
public sealed class ForumsModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ForumMapper>();
        services.AddScoped<AnnouncementNotifier>();
        services.AddScoped<IAssetReferenceReader, ForumAssetReader>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ForumThreadEndpoints.Map(app);
        ForumPostEndpoints.Map(app);
        AnnouncementEndpoints.Map(app);
    }
}
