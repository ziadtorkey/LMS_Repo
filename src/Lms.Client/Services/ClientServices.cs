using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<PermissionState>();
        services.AddScoped<Announcer>();
        services.AddScoped<IYearStore, YearStore>();
        services.AddScoped<INotificationFeed, NotificationFeed>();
        services.AddSingleton<Lms.Client.Services.Packages.PackagePollOptions>();

        if (configuration.GetValue<bool>("Lms:UseFakeApi"))
        {
            services.AddScoped<ILmsApi, FakeLmsApi>();
            services.AddScoped<IAuditApi, FakeAuditApi>();
            services.AddScoped<IAccessAdminApi, FakeAccessAdminApi>();
            services.AddScoped<IUnitLookupApi, FakeUnitLookupApi>();
            services.AddScoped<IYearsApi, FakeYearsApi>();
            services.AddScoped<INotificationsApi, FakeNotificationsApi>();
            services.AddSingleton<IImportsApi, FakeImportsApi>();
            services.AddScoped<INotificationConnection, FakeNotificationConnection>();
            services.AddSingleton<IClientsApi, FakeClientsApi>();
            services.AddSingleton<IOrgApi, FakeOrgApi>();
            services.AddSingleton<ITemplatesApi, FakeTemplatesApi>();
            services.AddSingleton<IConfigApi, FakeConfigApi>();
            services.AddSingleton<IStaffApi, FakeStaffApi>();
            services.AddSingleton<IStudentsApi, FakeStudentsApi>();
            services.AddSingleton<IMyProfileApi, FakeMyProfileApi>();
            services.AddSingleton<IChangeRequestsApi, FakeChangeRequestsApi>();
            services.AddSingleton<IGuardiansApi, FakeGuardiansApi>();
            services.AddSingleton<IClassesApi, FakeClassesApi>();
            services.AddSingleton<IClassCoursesApi, FakeClassCoursesApi>();
            services.AddSingleton<IIdentityProvidersApi, FakeIdentityProvidersApi>();
            services.AddSingleton<ICoursesApi, FakeCoursesApi>();
            services.AddSingleton<IBlueprintsApi>(new FakeBlueprintsApi { AutoSeed = true });
            services.AddSingleton<ILessonPlansApi, FakeLessonPlansApi>();
            services.AddSingleton<IFileUploadApi, FakeFileUploadApi>();
            services.AddSingleton<ILibraryApi, FakeLibraryApi>();
            services.AddSingleton<IForumsApi, FakeForumsApi>();
            services.AddSingleton<IAssetsApi, FakeAssetsApi>();
            services.AddSingleton<Lms.Client.Services.Packages.IPackagesApi, Lms.Client.Services.Packages.FakePackagesApi>();
            services.AddSingleton<IModulesApi, FakeModulesApi>();
            services.AddSingleton<IActivityOverviewApi, FakeActivityOverviewApi>();
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
        services.AddScoped<IAccessAdminApi, AccessAdminApi>();
        services.AddScoped<IUnitLookupApi, UnitLookupApi>();
        services.AddScoped<IYearsApi, YearsApi>();
        services.AddScoped<INotificationsApi, NotificationsApi>();
        services.AddScoped<IImportsApi, ImportsApi>();
        services.AddScoped<INotificationConnection, NotificationConnection>();
        services.AddHttpClient<ILmsApi, LmsApiClient>(c => c.BaseAddress = new Uri(baseAddress))
            .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>()
            .AddHttpMessageHandler<ForbiddenRedirectHandler>();

        // Module APIs share one authorized HttpClient through ApiHttp.
        services.AddHttpClient<ApiHttp>(c => c.BaseAddress = new Uri(baseAddress))
            .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>()
            .AddHttpMessageHandler<ForbiddenRedirectHandler>();
        services.AddScoped<IClientsApi, ClientsApi>();
        services.AddScoped<IOrgApi, OrgApi>();
        services.AddScoped<ITemplatesApi, TemplatesApi>();
        services.AddScoped<IConfigApi, ConfigApi>();
        services.AddScoped<IStaffApi, StaffApi>();
        services.AddScoped<IStudentsApi, StudentsApi>();
        services.AddScoped<IMyProfileApi, MyProfileApi>();
        services.AddScoped<IChangeRequestsApi, ChangeRequestsApi>();
        services.AddScoped<IGuardiansApi, GuardiansApi>();
        services.AddScoped<IClassesApi, ClassesApi>();
        services.AddScoped<IClassCoursesApi, ClassCoursesApi>();
        services.AddScoped<IIdentityProvidersApi, IdentityProvidersApi>();
        services.AddScoped<ICoursesApi, CoursesApi>();
        services.AddScoped<IBlueprintsApi, BlueprintsApi>();
        services.AddScoped<ILessonPlansApi, LessonPlansApi>();
        services.AddScoped<IFileUploadApi, FileUploadApi>();
        services.AddScoped<ILibraryApi, LibraryApi>();
        services.AddScoped<IForumsApi, ForumsApi>();
        services.AddScoped<IAssetsApi, AssetsApi>();
        services.AddScoped<Lms.Client.Services.Packages.IPackagesApi, Lms.Client.Services.Packages.PackagesApi>();
        services.AddScoped<IModulesApi, ModulesApi>();
        services.AddScoped<IActivityOverviewApi, ActivityOverviewApi>();
    }
}
