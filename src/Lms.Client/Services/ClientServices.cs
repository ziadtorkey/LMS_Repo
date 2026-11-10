using Lms.Client.Services.Api;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Lms.Client.Services;

public static class ClientServices
{
    /// <summary>
    /// Registers the client. The fake API/auth are used ONLY when configuration has an explicit
    /// Lms:UseFakeApi=true (wwwroot/appsettings.Standalone.json, standalone dev server) — never by environment alone.
    /// </summary>
    public static void Register(IServiceCollection services, IConfiguration configuration, string baseAddress)
    {
        services.AddLocalization();
        services.AddMudServices();
        services.AddTransient<MudBlazor.MudLocalizer, LmsMudLocalizer>();
        services.AddScoped<ICultureService, CultureService>();
        // Singleton on purpose: IHttpClientFactory builds its message handlers in their own DI scope, and the
        // handler must see (and clear) the same stored context as the UI.
        services.AddSingleton<ContextStore>();
        services.AddSingleton<IContextStore>(sp => sp.GetRequiredService<ContextStore>());
        services.AddScoped<PermissionState>();

        if (configuration.GetValue<bool>("Lms:UseFakeApi"))
        {
            services.AddScoped<ILmsApi, FakeLmsApi>();
            services.AddScoped<IAuditApi, FakeAuditApi>();
            services.AddScoped<AuthenticationStateProvider, FakeAuthenticationStateProvider>();
            services.AddAuthorizationCore();
            return;
        }

        services.AddOidcAuthentication(options =>
        {
            options.ProviderOptions.Authority = baseAddress.TrimEnd('/');
            options.ProviderOptions.ClientId = "lms-wasm";
            options.ProviderOptions.ResponseType = "code";
            options.ProviderOptions.DefaultScopes.Add("lms_api");
        });
        services.AddTransient<ForbiddenRedirectHandler>();
        services.AddScoped<IAuditApi, AuditApi>();
        services.AddHttpClient<ILmsApi, LmsApiClient>(c => c.BaseAddress = new Uri(baseAddress))
            .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>()
            .AddHttpMessageHandler<ForbiddenRedirectHandler>();
        services.AddHttpClient<ApiHttp>(c => c.BaseAddress = new Uri(baseAddress))
            .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>()
            .AddHttpMessageHandler<ForbiddenRedirectHandler>();
    }
}
