using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Lms.Domain.Identity;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Validation.AspNetCore;

namespace Lms.IntegrationTests.Auth;

/// <summary>Drives the browser-style login flow against the in-memory API: cookies on, redirects off.</summary>
internal sealed class AuthTestHost
{
    public const string Password = "Passw0rd1";
    public const string RemoteIp = "203.0.113.9";
    public const string RedirectUri = "http://localhost/authentication/login-callback";

    private readonly WebApplicationFactory<Program> _host;

    /// <param name="permitLimit">Requests per minute the auth rate limiter allows (high by default so flows can repeat).</param>
    /// <param name="remoteIp">Address the in-memory server reports; rate limit tests use their own to stay isolated.</param>
    /// <param name="resetLifespan">Overrides the reset/default token lifespan (the real one is 60 minutes).</param>
    public AuthTestHost(
        LmsApiFactory factory, int permitLimit = 1000, string remoteIp = RemoteIp, TimeSpan? resetLifespan = null)
    {
        Emails = new FakeEmailSender();
        // A fixed remote address: the in-memory test server has none, but login events must record one.
        _host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Auth:PermitLimit", permitLimit.ToString());
            builder.UseSetting("App:PublicBaseUrl", "http://localhost");
            builder.ConfigureServices(services =>
        {
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(new RemoteIpStartupFilter(remoteIp));
            services.AddSingleton<Lms.Domain.Abstractions.IEmailSender>(Emails);
            if (resetLifespan is { } lifespan)
            {
                services.Configure<Microsoft.AspNetCore.Identity.DataProtectionTokenProviderOptions>(o => o.TokenLifespan = lifespan);
            }

            // The shared factory installs a header-driven test scheme as the default; these tests exercise the real
            // OpenIddict bearer validation instead.
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                var scheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
                options.DefaultScheme = scheme;
                options.DefaultAuthenticateScheme = scheme;
                options.DefaultChallengeScheme = scheme;
            });
            });
        });
    }

    public FakeEmailSender Emails { get; }

    public IServiceProvider Services => _host.Services;

    public HttpClient NewBrowser() => _host.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public async Task<User> CreateUserAsync(
        string? email = null, bool platformAdmin = false, UserStatus status = UserStatus.Active)
    {
        email ??= $"user-{Guid.NewGuid():N}@lms.test";
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayNameEn = "Test User",
            DisplayNameAr = "مستخدم اختبار",
            IsPlatformAdmin = platformAdmin,
            Status = status,
        };
        var result = await users.CreateAsync(user, Password);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        return user;
    }

    public static (string Verifier, string Challenge) NewPkcePair()
    {
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (verifier, challenge);
    }

    public static string AuthorizeUrl(string challenge, string scope = "openid profile lms_api") =>
        "/connect/authorize?client_id=lms-wasm&response_type=code&scope=" + Uri.EscapeDataString(scope)
        + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
        + "&code_challenge=" + challenge + "&code_challenge_method=S256&state=xyz";

    /// <summary>GETs the login page (for the antiforgery token) and posts the credentials.</summary>
    public static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient browser, string email, string password, string? returnUrl = null, string loginUrl = "/Account/Login")
    {
        var page = await browser.GetAsync(loginUrl);
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "antiforgery token not found on the login page");

        var form = new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = token,
        };
        if (returnUrl is not null)
        {
            form["ReturnUrl"] = returnUrl;
        }

        return await browser.PostAsync(loginUrl, new FormUrlEncodedContent(form));
    }

    /// <summary>Runs the whole browser flow for the user and returns the issued access and refresh tokens.</summary>
    public async Task<(string Access, string Refresh)> SignInForTokensAsync(User user)
    {
        using var browser = NewBrowser();
        var (verifier, challenge) = NewPkcePair();
        var authorizeUrl = AuthorizeUrl(challenge, "openid profile offline_access lms_api");
        var login = await PostLoginAsync(browser, user.Email!, Password, authorizeUrl);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var back = await browser.GetAsync(login.Headers.Location);
        var code = System.Web.HttpUtility.ParseQueryString(back.Headers.Location!.Query)["code"]!;
        var token = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "lms-wasm",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = verifier,
        }));
        var body = System.Text.Json.JsonDocument.Parse(await token.Content.ReadAsStringAsync()).RootElement;
        return (body.GetProperty("access_token").GetString()!, body.GetProperty("refresh_token").GetString()!);
    }

    public Task DeactivateAsync(User user) => SetStatusAsync(user, UserStatus.Deactivated);

    public async Task SetStatusAsync(User user, UserStatus status)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var stored = (await users.FindByIdAsync(user.Id.ToString()))!;
        stored.Status = status;
        Assert.True((await users.UpdateAsync(stored)).Succeeded);
    }

    /// <summary>GETs the page (for the antiforgery token) and posts the fields back to the same url.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient browser, string url, Dictionary<string, string> fields, string? tokenFrom = null)
    {
        var page = await browser.GetAsync(tokenFrom ?? url);
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "antiforgery token not found on " + url);
        var form = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token };
        return await browser.PostAsync(url, new FormUrlEncodedContent(form));
    }

    /// <summary>The first link in an email body, as a path and query for the in-memory server.</summary>
    public static string FirstLink(Lms.Domain.Abstractions.EmailMessage message)
    {
        var href = WebUtility.HtmlDecode(Regex.Match(message.HtmlBody, "href=\"([^\"]+)\"").Groups[1].Value);
        return new Uri(href).PathAndQuery;
    }

    /// <summary>The text of an element carrying the given data-testid.</summary>
    public static string TestId(string html, string id) =>
        WebUtility.HtmlDecode(Regex.Match(html, $"data-testid=\"{id}\"[^>]*>([^<]*)<").Groups[1].Value).Trim();

    public static string ExtractError(string html) =>
        Regex.Match(html, "data-testid=\"login-error\">([^<]*)<").Groups[1].Value;

    private sealed class RemoteIpStartupFilter(string ip) : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(
            Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) =>
            app =>
            {
                app.Use((context, nxt) =>
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
                    return nxt(context);
                });
                next(app);
            };
    }
}
