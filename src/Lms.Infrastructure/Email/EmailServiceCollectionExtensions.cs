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
            o.SmtpHost = section["SmtpHost"] ?? o.SmtpHost;
            o.SmtpPort = int.TryParse(section["SmtpPort"], out var port) ? port : o.SmtpPort;
            o.From = section["From"] ?? o.From;
            o.FromName = section["FromName"] ?? o.FromName;
            o.UseStartTls = bool.TryParse(section["UseStartTls"], out var tls) && tls;
            o.UserName = section["UserName"];
            o.Password = section["Password"];
        });
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }
}
