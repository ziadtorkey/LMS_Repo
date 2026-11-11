using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Audit;

/// <summary>Audit viewer: registers the export limit and maps the endpoints (discovered automatically).</summary>
public sealed class AuditModule : ILmsModule
{
    public const int DefaultExportMaxRows = 50_000;

    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton(new AuditExportLimit(
            int.TryParse(configuration["Audit:ExportMaxRows"], out var n) && n > 0 ? n : DefaultExportMaxRows));

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapAuditEndpoints();
}

/// <summary>Largest number of rows one export may contain (configurable only so tests need not create 50,000 rows).</summary>
public sealed record AuditExportLimit(int MaxRows);
