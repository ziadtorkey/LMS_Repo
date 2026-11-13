using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Guardians;

/// <summary>Guardians of students (FR-STU-06, D30): linking, siblings, primary, the mandatory setting, matching and visibility.</summary>
public sealed class GuardiansModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        StudentGuardianEndpoints.Map(app);
        GuardianEndpoints.Map(app);
        GuardianPortalEndpoints.Map(app);
    }
}
