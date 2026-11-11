using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Templates;

/// <summary>Branch templates (FR-ORG-07/08): save, library, apply with placeholders. Uses the branch services.</summary>
public sealed class TemplatesModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapTemplateEndpoints();
}
