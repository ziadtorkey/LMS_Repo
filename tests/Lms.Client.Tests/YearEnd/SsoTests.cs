using System.Globalization;
using Bunit;
using Lms.Client.Components.Sso;
using Lms.Client.Services.Sso;
using Lms.Client.Tests.Support;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.YearEnd;

/// <summary>M7 H7: SAML 2.0 and LDAP provider settings (FR-SEC-08, D85) against the provisional SSO fake: own fields per kind, write-only secrets, a plain connection test.</summary>
public sealed class SsoTests : ClientTestContext
{
    private readonly FakeSsoProvidersApi _api = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public SsoTests() => Services.AddSingleton<ISsoProvidersApi>(_api);

    private void Open(string culture = "en", SsoProviderDto? existing = null)
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        var parameters = new DialogParameters<SsoProviderDialog> { { x => x.Existing, existing } };
        _ = Services.GetRequiredService<IDialogService>().ShowAsync<SsoProviderDialog>("t", parameters);
        _dialogs.WaitForElement("[data-testid=sso-dialog]");
    }

    private async Task PickKindAsync(string kind) =>
        await _dialogs.InvokeAsync(() => _dialogs.FindComponents<MudSelect<string>>().First().Instance.ValueChanged.InvokeAsync(kind));

    private static SsoProviderDto SavedLdap(bool stored) => new(Guid.NewGuid(), SsoKinds.Ldap, "ad", "الدليل", "Directory", true, null,
        new LdapSettingsDto("ldap.school.test", 636, LdapTransports.Ldaps, "cn=svc,dc=school", stored, "dc=school", "(uid={0})", "mail", ""),
        new SpValuesDto("a", "b", "c"));

    [Fact]
    public void Saml_kind_shows_only_saml_fields_and_the_sp_values_with_copy()
    {
        var saved = new SsoProviderDto(Guid.NewGuid(), SsoKinds.Saml, "sso", "دخول", "SSO", true,
            new SamlSettingsDto("https://idp.test/meta", "https://idp.test", "https://idp.test/sso", "AB:CD", new DateOnly(2030, 1, 1), "emailAddress", "mail", ""), null,
            new SpValuesDto("https://lms.test/saml/sso/acs", "https://lms.test/saml/sso", "https://lms.test/saml/sso/metadata"));
        Open(existing: saved);

        Assert.NotEmpty(_dialogs.FindAll("[data-testid=sso-saml-fields]"));
        Assert.Empty(_dialogs.FindAll("[data-testid=sso-ldap-fields]"));
        foreach (var id in new[] { "sso-sp-acs", "sso-sp-entity", "sso-sp-metadata" })
        {
            Assert.NotEmpty(_dialogs.FindAll($"[data-testid={id}-copy]")); // each value we give the identity team can be copied
            Assert.Equal("ltr", _dialogs.Find($"[data-testid={id}-value]").GetAttribute("dir"));
        }
        Assert.Contains("https://lms.test/saml/sso/acs", _dialogs.Find("[data-testid=sso-sp-acs-value]").TextContent);
        Assert.Empty(_dialogs.FindAll("input[type=password]")); // SAML has no password field at all
    }

    [Fact]
    public async Task Ldap_kind_shows_only_ldap_fields()
    {
        Open();

        await PickKindAsync(SsoKinds.Ldap);

        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=sso-ldap-fields]")));
        Assert.Empty(_dialogs.FindAll("[data-testid=sso-saml-fields]"));
        Assert.Empty(_dialogs.FindAll("[data-testid=sso-sp-acs]"));
    }

    [Fact]
    public async Task Ldap_has_no_plain_transport_option()
    {
        Open();
        await PickKindAsync(SsoKinds.Ldap);
        _dialogs.WaitForElement("[data-testid=sso-ldap-transport]");

        var options = _dialogs.FindAll("[data-testid^=sso-ldap-transport-]").Select(e => e.GetAttribute("data-testid")!["sso-ldap-transport-".Length..]).ToArray();

        Assert.Equal([LdapTransports.Ldaps, LdapTransports.StartTls], options);
        Assert.NotEmpty(_dialogs.FindAll("[data-testid=sso-ldap-no-plain]")); // and the screen says so
    }

    [Fact]
    public void Bind_password_is_write_only_and_never_rendered()
    {
        Open(existing: SavedLdap(stored: true));

        var field = _dialogs.Find("input[data-testid=sso-ldap-bind-password]");
        Assert.Equal("password", field.GetAttribute("type"));
        Assert.True(string.IsNullOrEmpty(field.GetAttribute("value"))); // the stored password is not read back
        Assert.Contains("Stored. Leave empty to keep it.", _dialogs.Markup);
        Assert.Equal("new-password", field.GetAttribute("autocomplete"));
    }

    [Fact]
    public async Task A_typed_password_is_sent_once_and_leaving_it_empty_keeps_the_stored_one()
    {
        var existing = SavedLdap(stored: true);
        await _api.UpdateAsync(existing.Id, new SaveSsoProviderRequest(SsoKinds.Ldap, "ad", "ا", "A", true, null, null, existing.Ldap, "S3cret!"));
        Open(existing: existing);

        await _dialogs.Find("[data-testid=sso-save]").ClickAsync(new()); // saved without typing a new password

        _dialogs.WaitForAssertion(() => Assert.Equal(2, _api.Saved.Count));
        Assert.Null(_api.Saved[^1].BindPassword); // nothing sent: the server keeps its own
        Assert.Equal("S3cret!", _api.Stored(existing.Id));
        Assert.DoesNotContain("S3cret!", _dialogs.Markup); // and it is never on screen
    }

    [Fact]
    public async Task Test_connection_shows_each_check_in_plain_words_and_no_secret()
    {
        Open();
        await PickKindAsync(SsoKinds.Ldap);
        _dialogs.WaitForElement("input[data-testid=sso-ldap-bind-password]");
        _dialogs.Find("input[data-testid=sso-ldap-bind-password]").Change("Topsecret#1");
        _dialogs.Find("input[data-testid=sso-ldap-host]").Change("bad.host.test");

        await _dialogs.Find("[data-testid=sso-test]").ClickAsync(new());

        _dialogs.WaitForElement("[data-testid=sso-test-result]");
        Assert.Equal("status", _dialogs.Find("[data-testid=sso-test-result]").GetAttribute("role"));
        Assert.Contains("did not answer", _dialogs.Find("[data-testid=sso-check-Reachable]").TextContent);
        Assert.Contains("bad.host.test", _dialogs.Find("[data-testid=sso-check-Reachable]").TextContent); // what failed, by name
        Assert.NotEmpty(_dialogs.Find("[data-testid=sso-check-Reachable]").QuerySelectorAll("svg")); // an icon and words, not colour alone
        Assert.DoesNotContain("Topsecret#1", _dialogs.Find("[data-testid=sso-test-result]").InnerHtml); // the result never repeats it (the masked field holds only what was just typed)
    }

    [Fact]
    public void Metadata_upload_accepts_xml_only()
    {
        Open();
        _dialogs.Find("[data-testid=sso-meta-upload-mode]").Click();
        var upload = _dialogs.FindComponent<InputFile>();

        _ = _dialogs.InvokeAsync(() => upload.UploadFiles(InputFileContent.CreateFromText("not xml", "metadata.json")));

        _dialogs.WaitForAssertion(() => Assert.Contains("Only XML files", _dialogs.Find("[data-testid=sso-metadata-error]").TextContent));
    }

    [Fact]
    public async Task A_big_xml_file_is_refused_with_the_cap()
    {
        Open();
        _dialogs.Find("[data-testid=sso-meta-upload-mode]").Click();
        var upload = _dialogs.FindComponent<InputFile>();

        await _dialogs.InvokeAsync(() => upload.UploadFiles(InputFileContent.CreateFromBinary(new byte[SsoLimits.MetadataMaxBytes + 1], "metadata.xml")));

        _dialogs.WaitForAssertion(() => Assert.Contains("512", _dialogs.Find("[data-testid=sso-metadata-error]").TextContent));
    }

    [Fact]
    public async Task Uploaded_metadata_xml_is_sent_with_the_provider()
    {
        Open();
        _dialogs.Find("[data-testid=sso-meta-upload-mode]").Click();
        var upload = _dialogs.FindComponent<InputFile>();
        await _dialogs.InvokeAsync(() => upload.UploadFiles(InputFileContent.CreateFromText("<EntityDescriptor>expiring</EntityDescriptor>", "metadata.xml")));
        _dialogs.Find("input[data-testid=sso-slug]").Change("sso");
        _dialogs.Find("input[data-testid=sso-sso-url]").Change("https://idp.test/sso");

        await _dialogs.Find("[data-testid=sso-save]").ClickAsync(new());

        _dialogs.WaitForAssertion(() => Assert.Single(_api.Saved));
        Assert.Equal("<EntityDescriptor>expiring</EntityDescriptor>", _api.Saved[0].SamlMetadataXml);
    }

    [Fact]
    public void The_certificate_chip_warns_only_inside_30_days()
    {
        Culture.Current = new CultureInfo("en");
        var today = DateOnly.FromDateTime(DateTime.Now);
        var soon = Render<CertificateExpiry>(p => p.Add(x => x.Fingerprint, "AB:CD").Add(x => x.Expires, today.AddDays(17)));
        var later = Render<CertificateExpiry>(p => p.Add(x => x.Fingerprint, "AB:CD").Add(x => x.Expires, today.AddDays(200)));
        var gone = Render<CertificateExpiry>(p => p.Add(x => x.Fingerprint, "AB:CD").Add(x => x.Expires, today.AddDays(-2)));

        Assert.Contains("17 days left", soon.Find("[data-testid=sso-cert-warning]").TextContent);
        Assert.Empty(later.FindAll("[data-testid=sso-cert-warning]"));
        Assert.Contains("Expired", gone.Find("[data-testid=sso-cert-warning]").TextContent);
        Assert.Equal("ltr", soon.Find("[data-testid=sso-cert-fingerprint]").GetAttribute("dir"));
    }

    [Fact]
    public void Dialog_renders_rtl_in_arabic_and_is_full_screen_on_phones()
    {
        Open("ar");

        Assert.True(Culture.IsRightToLeft);
        Assert.NotEmpty(_dialogs.FindAll(".lms-dialog-fs"));
        Assert.Contains("SAML 2.0", _dialogs.Markup);
        foreach (var input in _dialogs.FindAll("input[data-testid=sso-slug], input[data-testid=sso-entity-id], input[data-testid=sso-sso-url]"))
        {
            Assert.Equal("ltr", input.Closest("[dir]")?.GetAttribute("dir") ?? input.GetAttribute("dir")); // URLs and ids keep their order
        }
    }
}
