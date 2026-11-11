using System.Net;
using System.Text.Json;
using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M2;

/// <summary>
/// M2 exit demo (plan Task 38): external sign-in through a client's OpenID Connect provider (FR-SEC-08, D33), against the
/// <c>mock-oidc</c> compose service. Each scenario creates its own provider through the API as the school's Org Admin (the
/// provider admin page is a separate task) and removes it afterwards, and signs the person in from a second, clean browser
/// context so no password session interferes.
/// </summary>
public sealed class ExternalLoginTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    private const string SchoolEn = "Altagamo3 Schools";

    private static string RunId() => DateTime.UtcNow.ToString("HHmmss") + Guid.NewGuid().ToString("N")[..4];

    /// <summary>The school's Org Admin in a browser context of its own; creates and deletes providers through the API.</summary>
    private sealed class AdminSession : IAsyncDisposable
    {
        private readonly IBrowserContext _context;
        private readonly List<Guid> _providers = [];

        private AdminSession(IBrowserContext context, E2EApi api)
        {
            _context = context;
            Api = api;
        }

        public E2EApi Api { get; }

        public static async Task<AdminSession> StartAsync(E2EFixture fixture)
        {
            var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
            {
                BaseURL = fixture.Settings.BaseUrl.ToString(),
                IgnoreHTTPSErrors = true,
            });
            context.SetDefaultTimeout(DefaultTimeoutMs);
            var page = await context.NewPageAsync();
            await new LoginPage(page).SignInAsync(E2ESettings.SeedUser("orgadmin"), fixture.Settings.Password);
            await new ShellPage(page).WaitReadyAsync();
            return new AdminSession(context, new E2EApi(page, fixture.Settings.BaseUrl));
        }

        /// <summary>Registers an enabled provider that points at the mock server; returns its id and slug.</summary>
        public async Task<(Guid Id, string Slug)> CreateProviderAsync(params string[] emailDomains)
        {
            var slug = "e2e-" + RunId();
            var created = await Api.JsonAsync(await Api.SendAsync("POST", "/api/identity-providers", new
            {
                kind = "Oidc",
                slug,
                displayNameAr = "مزود E2E",
                displayNameEn = "E2E provider",
                authority = MockOidc.Authority,
                azureTenantId = (string?)null,
                clientId = MockOidc.ClientId,
                clientSecret = MockOidc.ClientSecret,
                scopes = "openid profile email",
                matchBy = "Email",
                nationalIdClaim = (string?)null,
                emailDomains,
                enabled = true,
            }));
            var id = created.GetProperty("id").GetGuid();
            _providers.Add(id);
            return (id, slug);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                foreach (var id in _providers)
                {
                    await Api.SendAsync("DELETE", $"/api/identity-providers/{id}");
                }
            }
            catch (PlaywrightException)
            {
                // Best effort: a provider left behind is harmless (unique slug) and the next run uses new ones.
            }

            await _context.DisposeAsync();
        }
    }

    /// <summary>
    /// A staff member types the work e-mail on the sign-in page; the domain picks the school's provider; signing in at the
    /// mock server as that address lands in the school with the role the person already holds (first sign-in links the account).
    /// </summary>
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Root_admin_configures_oidc_provider_tests_connection_and_staff_signs_in_with_organization_account(string culture)
    {
        await StartAsync(culture: culture);
        await using var admin = await AdminSession.StartAsync(Fixture);
        var (providerId, _) = await admin.CreateProviderAsync(E2ESettings.SeedDomain);

        // Test connection (the API behind the page's button): the mock answers its discovery document.
        var test = await admin.Api.JsonAsync(await admin.Api.SendAsync("POST", $"/api/identity-providers/{providerId}/test"));
        Assert.True(test.GetProperty("ok").GetBoolean(), test.ToString());
        Assert.Equal(MockOidc.Authority, test.GetProperty("issuer").GetString());

        // The person: sign-in page → "organization account" → work e-mail → provider → back in the app.
        var login = new LoginPage(Page);
        await Page.GotoAsync("/");
        await login.Email.WaitForAsync();
        await Page.GetByTestId("external-login-link").ClickAsync();
        await Page.GetByTestId("external-email").FillAsync(E2ESettings.SeedUser("teacher"));
        await Page.GetByTestId("external-continue").ClickAsync();
        await MockOidc.SignInAsync(Page, "e2e-staff-" + RunId(), E2ESettings.SeedUser("teacher"));

        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();
        var me = await new E2EApi(Page, Settings.BaseUrl).JsonAsync(await new E2EApi(Page, Settings.BaseUrl).SendAsync("GET", "/api/me", contextId: Guid.Empty));
        var contexts = me.GetProperty("contexts").EnumerateArray().ToList();
        var own = Assert.Single(contexts);
        Assert.Equal(SchoolEn, own.GetProperty("organizationNameEn").GetString());
        Assert.Equal("Teacher", own.GetProperty("roleNameEn").GetString());
        Assert.Equal("Section 1", own.GetProperty("orgUnitNameEn").GetString()); // teacher@ is a Teacher at KG-S1
    }

    /// <summary>
    /// A valid identity at the provider that nobody in the client owns is refused with one generic message on the sign-in
    /// page; no account is created and no session starts.
    /// </summary>
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Unknown_identity_sees_not_found_message(string culture)
    {
        await StartAsync(culture: culture);
        await using var admin = await AdminSession.StartAsync(Fixture);
        var (_, slug) = await admin.CreateProviderAsync();
        var run = RunId();

        await Page.GotoAsync($"/Account/ExternalLogin/{slug}");
        await MockOidc.SignInAsync(Page, "e2e-stranger-" + run, $"nobody-{run}@{E2ESettings.SeedDomain}");

        var login = new LoginPage(Page);
        await Expect(login.Error).ToHaveTextAsync(Resx.Account(culture, "ExternalNotFound"));
        Assert.Contains("/Account/Login", Page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(await Page.Context.CookiesAsync(), c => c.Name.StartsWith(".AspNetCore.Identity", StringComparison.Ordinal));
    }

    /// <summary>
    /// Review Focus 2: a person who belongs to the school <b>and</b> the university signs in through the school's provider.
    /// The session sees only the school's context, and the API refuses the university's context for it (403 OutOfScope),
    /// while the same person signing in with a password sees both.
    /// </summary>
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Dual_client_user_signed_in_via_client_A_provider_does_not_see_client_B_context(string culture)
    {
        await StartAsync(culture: culture);
        await using var admin = await AdminSession.StartAsync(Fixture);
        var (_, slug) = await admin.CreateProviderAsync();
        var dual = E2ESettings.SeedUser("dual-e2e");

        // Control: with a password the same person has both contexts (and learns the university context id).
        var universityContext = await UniversityContextOfPasswordSessionAsync(dual);

        await Page.GotoAsync($"/Account/ExternalLogin/{slug}");
        await MockOidc.SignInAsync(Page, "e2e-dual-" + RunId(), dual);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();

        var api = new E2EApi(Page, Settings.BaseUrl);
        var me = await api.JsonAsync(await api.SendAsync("GET", "/api/me", contextId: Guid.Empty));
        var contexts = me.GetProperty("contexts").EnumerateArray().ToList();
        var own = Assert.Single(contexts);
        Assert.Equal(SchoolEn, own.GetProperty("organizationNameEn").GetString());
        Assert.DoesNotContain(contexts, c => c.GetProperty("assignmentId").GetGuid() == universityContext);

        var refused = await api.SendAsync("GET", "/api/me/permissions", contextId: universityContext);
        Assert.Equal((int)HttpStatusCode.Forbidden, refused.Status);
        Assert.Contains("OutOfScope", await refused.TextAsync());

        // The app itself offers nothing but the school: the context chooser lists a single option at most.
        await Page.GotoAsync("/choose-context");
        Assert.DoesNotContain("MNF", await Page.Locator("body").InnerTextAsync());
    }

    /// <summary>A password session of <paramref name="email"/> in a throw-away context: its university assignment id.</summary>
    private async Task<Guid> UniversityContextOfPasswordSessionAsync(string email)
    {
        var context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Settings.BaseUrl.ToString(),
            IgnoreHTTPSErrors = true,
        });
        context.SetDefaultTimeout(DefaultTimeoutMs);
        try
        {
            var page = await context.NewPageAsync();
            await new LoginPage(page).SignInAsync(email, Settings.Password);
            var chooser = new ChooseContextPage(page);
            await chooser.WaitAsync();
            await Expect(chooser.Options).ToHaveCountAsync(2); // school and university
            var api = new E2EApi(page, Settings.BaseUrl);
            var me = await api.JsonAsync(await api.SendAsync("GET", "/api/me", contextId: Guid.Empty));
            return me.GetProperty("contexts").EnumerateArray()
                .Single(c => c.GetProperty("organizationNameEn").GetString() != SchoolEn)
                .GetProperty("assignmentId").GetGuid();
        }
        finally
        {
            await context.DisposeAsync();
        }
    }
}
