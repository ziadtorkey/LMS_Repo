using Lms.Domain.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Email;

public static class EmailServiceCollectionExtensions
{
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailOptions>(o =>
        {
            var section = configuration.GetSection("Email");
            o.Provider = section["Provider"] ?? o.Provider;
            o.SmtpHost = section["SmtpHost"] ?? o.SmtpHost;
            o.SmtpPort = int.TryParse(section["SmtpPort"], out var port) ? port : o.SmtpPort;
            o.From = section["From"] ?? o.From;
            o.FromName = section["FromName"] ?? o.FromName;
            o.UseStartTls = bool.TryParse(section["UseStartTls"], out var tls) && tls;
            o.UserName = section["UserName"];
            o.Password = section["Password"];
        });
        var provider = configuration.GetSection("Email")["Provider"] ?? "Smtp";
        if (!provider.Equals("Smtp", StringComparison.OrdinalIgnoreCase) && !provider.Equals("Log", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Email:Provider '{provider}' is not known. Use Smtp or Log; a production relay needs its own adapter (D67).");
        }

        services.AddSingleton<IEmailSender>(sp => new GuardedEmailSender(provider.Equals("Log", StringComparison.OrdinalIgnoreCase)
            ? ActivatorUtilities.CreateInstance<LogEmailSender>(sp)
            : ActivatorUtilities.CreateInstance<SmtpEmailSender>(sp)));
        return services;
    }
}
