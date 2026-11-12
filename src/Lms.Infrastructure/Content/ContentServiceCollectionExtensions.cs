using Lms.Domain.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Content;

public static class ContentServiceCollectionExtensions
{
    /// <summary>The rich-text sanitiser and the asset service (M4a foundations).</summary>
    public static IServiceCollection AddContent(this IServiceCollection services)
    {
        services.AddSingleton<IRichTextSanitizer, RichTextSanitizer>();
        services.AddScoped<IAssetService, AssetService>();
        return services;
    }
}
