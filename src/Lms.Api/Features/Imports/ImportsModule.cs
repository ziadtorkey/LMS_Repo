using System.Globalization;
using System.Resources;
using Lms.Api.Infrastructure;
using Lms.Api.Resources;
using Lms.Infrastructure.Imports;

namespace Lms.Api.Features.Imports;

/// <summary>
/// The import framework (X-06): template, upload, preview, all-or-nothing commit, error report. Endpoints exist for every
/// kind registered with <c>AddImportDefinition&lt;T&gt;()</c> (staff in M2 Task 17); this module registers none itself.
/// </summary>
public sealed class ImportsModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddImports();
        services.AddScoped<ImportQueries>();
        services.AddSingleton<IImportMessages, ImportMessages>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapImportEndpoints();
}

/// <summary>Error messages from <c>ImportResource.{ar,en}.resx</c> in the culture of the batch, not of the caller.</summary>
public sealed class ImportMessages : IImportMessages
{
    private static readonly ResourceManager Resources = new(typeof(ImportResource).FullName!, typeof(ImportResource).Assembly);

    public string Format(string code, string culture, IReadOnlyDictionary<string, string>? args)
    {
        var info = CultureInfo.GetCultureInfo(culture.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en");
        var text = Resources.GetString("ImportError_" + code, info) ?? code;
        if (args is not null)
        {
            foreach (var (key, value) in args)
            {
                text = text.Replace("{" + key + "}", value, StringComparison.Ordinal);
            }
        }

        return text;
    }
}
