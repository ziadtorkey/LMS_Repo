using Lms.Domain.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Sms;

public static class SmsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SMS provider chosen by <c>Sms:Provider</c> (D68). Only <c>Fake</c> exists (the default); any other value stops the startup with a message, so a
    /// production host can never silently run without a gateway or with a made-up one. The owner picks the Saudi-licensed gateway before go-live and its adapter adds
    /// a case here.
    /// </summary>
    public static IServiceCollection AddSms(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration.GetSection("Sms")["Provider"] ?? "Fake";
        if (!string.Equals(provider, "Fake", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Sms:Provider '{provider}' is not known. Only Fake exists; a gateway adapter is added before go-live (D68).");
        }

        services.AddSingleton<FakeSmsProvider>();
        services.AddSingleton<ISmsProvider>(sp => sp.GetRequiredService<FakeSmsProvider>());
        return services;
    }
}
