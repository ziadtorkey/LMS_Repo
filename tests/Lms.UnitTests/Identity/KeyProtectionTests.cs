using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Lms.Infrastructure.Identity.External;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.UnitTests.Identity;

public sealed class KeyProtectionTests : IDisposable
{
    private const string Password = "test-pfx-password";
    private readonly string _dir = Directory.CreateTempSubdirectory("lms-keys-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WritePfx(string name, DateTimeOffset? notAfter = null, bool withPrivateKey = true)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=lms-key-protection-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), notAfter ?? DateTimeOffset.UtcNow.AddYears(1));
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, withPrivateKey ? cert.Export(X509ContentType.Pfx, Password) : cert.Export(X509ContentType.Cert));
        return path;
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    private static (string, string)[] Cert(string path, string prefix = "DataProtection:Certificate") =>
        [($"{prefix}:Path", path), ($"{prefix}:Password", Password)];

    [Fact]
    public void Outside_Development_a_missing_certificate_stops_startup_with_a_clear_message()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => KeyProtection.Apply(new ServiceCollection().AddDataProtection(), Config(), isDevelopment: false));

        Assert.Contains("DataProtection:Certificate", ex.Message);
        Assert.Contains("Development", ex.Message);
    }

    [Fact]
    public void Development_without_a_certificate_keeps_the_plain_key_ring()
    {
        var repository = new InMemoryXmlRepository();
        var provider = Provider(Config(), isDevelopment: true, repository);

        provider.CreateProtector("p").Protect("x");

        Assert.DoesNotContain(repository.Elements, e => e.ToString().Contains("EncryptedData"));
    }

    [Fact]
    public void Outside_Development_keys_are_stored_encrypted_with_the_certificate()
    {
        var path = WritePfx("current.pfx");
        var repository = new InMemoryXmlRepository();
        var provider = Provider(Config(Cert(path)), isDevelopment: false, repository);

        var cipher = provider.CreateProtector("p").Protect("secret");

        Assert.NotEmpty(repository.Elements);
        Assert.All(repository.Elements.Where(e => e.Name.LocalName == "key"), e =>
        {
            Assert.Contains("EncryptedData", e.ToString());
            Assert.DoesNotContain("masterKey", e.ToString().Replace("EncryptedData", string.Empty));
        });
        Assert.Equal("secret", provider.CreateProtector("p").Unprotect(cipher));
    }

    [Fact]
    public void Previous_certificate_still_reads_keys_after_rotation()
    {
        var oldPath = WritePfx("old.pfx");
        var repository = new InMemoryXmlRepository();
        var cipher = Provider(Config(Cert(oldPath)), false, repository).CreateProtector("p").Protect("kept");

        var newPath = WritePfx("new.pfx");
        var rotated = Provider(Config([.. Cert(newPath), .. Cert(oldPath, "DataProtection:PreviousCertificates:0")]), false, repository);

        Assert.Equal("kept", rotated.CreateProtector("p").Unprotect(cipher));
    }

    [Fact]
    public void Without_the_previous_certificate_old_keys_cannot_be_read()
    {
        var oldPath = WritePfx("old.pfx");
        var repository = new InMemoryXmlRepository();
        var cipher = Provider(Config(Cert(oldPath)), false, repository).CreateProtector("p").Protect("lost");

        var rotated = Provider(Config(Cert(WritePfx("new.pfx"))), false, repository);

        Assert.ThrowsAny<CryptographicException>(() => rotated.CreateProtector("p").Unprotect(cipher));
    }

    [Fact]
    public void Base64_certificate_is_accepted()
    {
        var bytes = File.ReadAllBytes(WritePfx("b64.pfx"));

        var cert = KeyProtection.LoadCertificate(Config(
            ("DataProtection:Certificate:Base64", Convert.ToBase64String(bytes)), ("DataProtection:Certificate:Password", Password)),
            "DataProtection:Certificate");

        Assert.True(cert!.HasPrivateKey);
    }

    [Fact]
    public void Expired_certificate_and_certificate_without_private_key_are_refused()
    {
        var expired = WritePfx("expired.pfx", DateTimeOffset.UtcNow.AddDays(-1));
        var publicOnly = WritePfx("public.cer", withPrivateKey: false);

        var e1 = Assert.Throws<InvalidOperationException>(() => KeyProtection.Apply(new ServiceCollection().AddDataProtection(), Config(Cert(expired)), false));
        var e2 = Assert.Throws<InvalidOperationException>(() => KeyProtection.Apply(new ServiceCollection().AddDataProtection(), Config(Cert(publicOnly)), false));

        Assert.Contains("expired", e1.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("could not be loaded", e2.Message); // a certificate-only file is not a PFX with a key
    }

    [Fact]
    public void Wrong_password_and_missing_file_are_reported_without_leaking_the_password()
    {
        var path = WritePfx("pw.pfx");

        var wrong = Assert.Throws<InvalidOperationException>(() => KeyProtection.LoadCertificate(
            Config(("DataProtection:Certificate:Path", path), ("DataProtection:Certificate:Password", "nope-nope")), "DataProtection:Certificate"));
        var missing = Assert.Throws<InvalidOperationException>(() => KeyProtection.LoadCertificate(
            Config(("DataProtection:Certificate:Path", Path.Combine(_dir, "none.pfx"))), "DataProtection:Certificate"));

        Assert.DoesNotContain("nope-nope", wrong.Message);
        Assert.Contains("none.pfx", missing.Message);
    }

    private static IDataProtectionProvider Provider(IConfiguration configuration, bool isDevelopment, IXmlRepository repository)
    {
        var services = new ServiceCollection();
        var builder = services.AddDataProtection().SetApplicationName("Lms-test");
        KeyProtection.Apply(builder, configuration, isDevelopment);
        services.Configure<KeyManagementOptions>(o => o.XmlRepository = repository);
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    private sealed class InMemoryXmlRepository : IXmlRepository
    {
        public List<XElement> Elements { get; } = [];

        public IReadOnlyCollection<XElement> GetAllElements() => Elements.ToList();

        public void StoreElement(XElement element, string friendlyName) => Elements.Add(element);
    }
}
