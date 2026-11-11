using System.Security.Cryptography.X509Certificates;
using Lms.Domain.Identity;
using Lms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Lms.Api.Features.Auth;

public static class AuthServiceCollectionExtensions
{
    public const string WasmClientId = "lms-wasm";
    public const string ApiScope = "lms_api";

    /// <summary>Identity cookies (login page), OpenIddict authorization server + validation, client seeding.</summary>
    public static IServiceCollection AddLmsAuth(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // The API authenticates bearer tokens by default; the Identity cookie is used only by the login/authorize pages.
        services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .AddIdentityCookies(cookies => cookies.ApplicationCookie?.Configure(options =>
            {
                options.LoginPath = "/Account/Login";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
            }));
        services.AddAuthorization();
        services.AddScoped<IUserClaimsPrincipalFactory<User>, UserClaimsPrincipalFactory<User>>();
        services.AddHttpContextAccessor();
        services.AddScoped<SignInManager<User>>();
        services.AddScoped<ISecurityStampValidator, SecurityStampValidator<User>>();

        services.AddLocalization();
        services.AddRazorPages();
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture("ar").AddSupportedCultures("ar", "en").AddSupportedUICultures("ar", "en");
            options.RequestCultureProviders =
            [
                new QueryStringRequestCultureProvider(),
                new CookieRequestCultureProvider(),
                new AcceptLanguageHeaderRequestCultureProvider(),
            ];
        });

        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<LmsDbContext>().ReplaceDefaultEntities<Guid>())
            .AddServer(options =>
            {
                options.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetEndSessionEndpointUris("/connect/logout")
                    .SetUserInfoEndpointUris("/connect/userinfo");

                // Authorization code + PKCE only (no password, implicit or client-credentials grants).
                options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().RequireProofKeyForCodeExchange();
                options.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.OfflineAccess, ApiScope);
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(30));

                ConfigureKeys(options, configuration, environment);

                var aspNetCore = options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough();
                if (environment.IsDevelopment())
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                // Tokens in URLs end up in logs and referrers, so the default ?access_token= extraction is off for the
                // whole API; the notification hub alone gets it back through HubAccessTokenStartupFilter.
                options.UseAspNetCore().DisableAccessTokenExtractionFromQueryString();
                options.AddEventHandler(ValidateUserIsActiveHandler.Descriptor);
            });

        services.AddHostedService<OpenIddictClientSeeder>();
        return services;
    }

    private static void ConfigureKeys(
        OpenIddictServerBuilder options, IConfiguration configuration, IHostEnvironment environment)
    {
        var signing = configuration["Auth:SigningCertificatePath"];
        var encryption = configuration["Auth:EncryptionCertificatePath"];
        var password = configuration["Auth:CertificatePassword"];

        if (!string.IsNullOrEmpty(signing) && !string.IsNullOrEmpty(encryption))
        {
            options.AddSigningCertificate(X509CertificateLoader.LoadPkcs12FromFile(signing, password));
            options.AddEncryptionCertificate(X509CertificateLoader.LoadPkcs12FromFile(encryption, password));
        }
        else if (environment.IsDevelopment())
        {
            // Ephemeral keys: tokens do not survive a restart, which is fine for local development.
            options.AddEphemeralSigningKey().AddEphemeralEncryptionKey();
        }
        else
        {
            throw new InvalidOperationException(
                "Auth:SigningCertificatePath and Auth:EncryptionCertificatePath must be configured outside Development.");
        }
    }
}
