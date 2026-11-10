using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Lms.Domain.Identity;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Auth;

/// <summary>Drives the browser-style login flow against the in-memory API: cookies on, redirects off.</summary>
internal sealed class AuthTestHost
{
    public const string Password = "Passw0rd1";
    public const string RemoteIp = "203.0.113.9";
    public const string RedirectUri = "http://localhost/authentication/login-callback";

    private readonly WebApplicationFactory<Program> _host;

    public AuthTestHost(LmsApiFactory factory)
    {
        // A fixed remote address: the in-memory test server has none, but login events must record one.
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(new RemoteIpStartupFilter(RemoteIp))));
    }

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

    public static string AuthorizeUrl(string challenge) =>
        "/connect/authorize?client_id=lms-wasm&response_type=code&scope=" + Uri.EscapeDataString("openid profile lms_api")
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
