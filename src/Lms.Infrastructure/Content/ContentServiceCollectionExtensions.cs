using Lms.Domain.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Content;

public static class ContentServiceCollectionExtensions
{
    /// <summary>The rich-text sanitiser (M4a).</summary>
    public static IServiceCollection AddContent(this IServiceCollection services)
    {
        services.AddSingleton<IRichTextSanitizer, RichTextSanitizer>();
        return services;
    }
}
