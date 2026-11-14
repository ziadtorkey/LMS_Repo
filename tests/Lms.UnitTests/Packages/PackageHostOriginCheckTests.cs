using Lms.Infrastructure.Packages;

namespace Lms.UnitTests.Packages;

/// <summary>D56 / Decision 10: the content host must be on another registrable domain than the app, or every package route fails closed.</summary>
public sealed class PackageHostOriginCheckTests
{
    [Theory]
    [InlineData("https://lms-content.example", "https://lms.example.com", true)]
    [InlineData("https://content.example.net", "https://app.example.com", true)]
    [InlineData("http://127.0.0.1:5200", "http://localhost:5100", true)]
    [InlineData("http://localhost:5200", "http://127.0.0.1:5100", true)]
    [InlineData("https://lms-content.example.sa", "https://school.example.com.sa", true)]
    public void A_content_host_on_another_site_is_accepted(string content, string app, bool expected) =>
        Assert.Equal(expected, PackageHostOriginCheck.IsSeparate(content, app));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("ftp://content.example.net")]
    [InlineData("/relative")]
    public void A_missing_or_malformed_origin_fails_closed(string? content) =>
        Assert.False(PackageHostOriginCheck.IsSeparate(content, "https://lms.example.com"));

    [Theory]
    [InlineData("https://lms.example.com", "https://lms.example.com")]
    [InlineData("https://lms.example.com:8443", "https://lms.example.com")]
    [InlineData("https://content.lms.example.com", "https://lms.example.com")]
    [InlineData("https://content.example.com", "https://lms.example.com")]
    [InlineData("https://example.com", "https://lms.example.com")]
    [InlineData("https://a.school.example.com.sa", "https://b.school.example.com.sa")]
    [InlineData("http://content.localhost:5200", "http://localhost:5100")]
    [InlineData("http://localhost:5200", "http://localhost:5100")]
    [InlineData("http://127.0.0.1:5200", "http://127.0.0.1:5100")]
    public void The_app_origin_or_one_of_its_sites_is_refused(string content, string app) =>
        Assert.False(PackageHostOriginCheck.IsSeparate(content, app));

    [Theory]
    [InlineData("lms.example.com", "example.com")]
    [InlineData("example.com.", "example.com")]
    [InlineData("school.example.com.sa", "example.com.sa")]
    [InlineData("a.b.example.co.uk", "example.co.uk")]
    [InlineData("localhost", "localhost")]
    [InlineData("x.localhost", "localhost")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("[::1]", "[::1]")]
    public void Registrable_domain(string host, string expected) => Assert.Equal(expected, PackageHostOriginCheck.RegistrableDomain(host));
}
