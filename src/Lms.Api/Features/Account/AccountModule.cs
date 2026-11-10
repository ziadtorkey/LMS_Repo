using System.Threading.RateLimiting;
using Lms.Api.Resources;
using Lms.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace Lms.Api.Features.Account;

/// <summary>Account recovery: password reset and invitation tokens, their emails, and the auth rate limit.</summary>
public static class AccountModule
{
    public const string AuthRateLimitPolicy = "auth";
    public const string ResetPasswordPath = "/Account/ResetPassword";

    public static IServiceCollection AddAccount(this IServiceCollection services, IConfiguration configuration)
    {
        // Reset links last 60 minutes (Identity:ResetTokenLifespanMinutes), invitation links 72 hours; both are tied to
        // the security stamp, which rotates on success, so each link works once.
        new IdentityBuilder(typeof(User), services).AddDefaultTokenProviders();
        services.Configure<IdentityOptions>(o =>
            o.Tokens.ProviderMap[InvitationTokenProvider.ProviderName] = new TokenProviderDescriptor(typeof(InvitationTokenProvider)));
        services.AddTransient<InvitationTokenProvider>();
        var resetMinutes = int.TryParse(configuration["Identity:ResetTokenLifespanMinutes"], out var m) && m > 0 ? m : 60;
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromMinutes(resetMinutes));

        services.Configure<AccountOptions>(o =>
            o.PublicBaseUrl = (configuration["App:PublicBaseUrl"] ?? o.PublicBaseUrl).TrimEnd('/'));
        services.AddScoped<IAccountEmails, AccountEmails>();

        // 5 POSTs per minute and IP (login and forgot-password); reads are never limited.
        var permitLimit = int.TryParse(configuration["RateLimiting:Auth:PermitLimit"], out var limit) && limit > 0 ? limit : 5;
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthRateLimitPolicy, context =>
                context.Request.Method == HttpMethods.Post
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = permitLimit,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                        })
                    : RateLimitPartition.GetNoLimiter("read"));
            options.OnRejected = async (context, cancellationToken) =>
            {
                var localizer = context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<AccountResource>>();
                context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                await context.HttpContext.Response.WriteAsync(localizer["TooManyRequests"], cancellationToken);
            };
        });

        return services;
    }
}

public sealed class AccountOptions
{
    /// <summary>Address users reach the site at; used for the links in emails.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5132";
}
