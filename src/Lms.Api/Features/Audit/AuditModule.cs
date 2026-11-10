namespace Lms.Api.Features.Audit;

/// <summary>Audit viewer settings.</summary>
public static class AuditModule
{
    public const int DefaultExportMaxRows = 50_000;

    public static IServiceCollection AddAuditViewer(this IServiceCollection services)
    {
        services.AddSingleton(sp => new AuditExportLimit(
            int.TryParse(sp.GetRequiredService<IConfiguration>()["Audit:ExportMaxRows"], out var n) && n > 0 ? n : DefaultExportMaxRows));
        return services;
    }
}

/// <summary>Largest number of rows one export may contain (configurable only so tests need not create 50,000 rows).</summary>
public sealed record AuditExportLimit(int MaxRows);
