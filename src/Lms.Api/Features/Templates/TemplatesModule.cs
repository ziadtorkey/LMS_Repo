namespace Lms.Api.Features.Templates;

/// <summary>Branch templates (FR-ORG-07/08): save, library, apply with placeholders. Uses the branch services.</summary>
public static class TemplatesModule
{
    public static IEndpointRouteBuilder MapTemplates(this IEndpointRouteBuilder app) => app.MapTemplateEndpoints();
}
