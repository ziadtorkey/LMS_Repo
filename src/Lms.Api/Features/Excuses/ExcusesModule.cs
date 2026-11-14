using Lms.Api.Infrastructure;
using Lms.Domain.Files;
using Lms.Infrastructure.Excuses;
using Microsoft.Extensions.Options;

namespace Lms.Api.Features.Excuses;

/// <summary>Absence excuses (M5 Task 19, FR-EXC-02/03, D61, D65). Submit, withdraw, approve and the own-data reads are here; the attachment upload and URL routes and the retention job follow.</summary>
public sealed class ExcusesModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExcuseAttachmentOptions>(configuration.GetSection(ExcuseAttachmentOptions.Section));
        services.AddScoped<ExcuseNotifier>();
        services.AddScoped<ExcuseSubmissionService>();
        services.AddScoped<ExcuseEscalationTargets>();
        services.AddSingleton<IFileScanner, NoOpFileScanner>(); // D65: a real scanner replaces this registration before production
        services.AddScoped(sp => new ExcuseAttachmentValidator(
            sp.GetRequiredService<IFileScanner>(), sp.GetRequiredService<IOptions<ExcuseAttachmentOptions>>().Value.ScanAttachments));
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ExcuseReviewEndpoints.Map(app);
        ExcuseSubmitEndpoints.Map(app);
        ExcuseApproveEndpoints.Map(app);
    }
}
