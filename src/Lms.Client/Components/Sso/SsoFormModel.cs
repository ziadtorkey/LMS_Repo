using Lms.Client.Services.Sso;

namespace Lms.Client.Components.Sso;

/// <summary>The editable state of the SAML and LDAP dialog. Secrets live only here while the dialog is open: the stored bind password is never read back (D85).</summary>
public sealed class SsoFormModel
{
    public string Kind { get; set; } = SsoKinds.Saml;
    public string Slug { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public bool Enabled { get; set; } = true;

    // SAML
    public bool UseUpload { get; set; }
    public string MetadataUrl { get; set; } = "";
    public string? MetadataXml { get; set; }
    public string? MetadataFileName { get; set; }
    public string EntityId { get; set; } = "";
    public string SsoUrl { get; set; } = "";
    public string? CertFingerprint { get; set; }
    public DateOnly? CertExpires { get; set; }
    public string NameIdFormat { get; set; } = "emailAddress";
    public string SamlEmailAttribute { get; set; } = "mail";
    public string SamlNationalIdAttribute { get; set; } = "";

    // LDAP
    public string Host { get; set; } = "";
    public int Port { get; set; } = 636;
    public string Transport { get; set; } = LdapTransports.Ldaps;
    public string BindDn { get; set; } = "";
    public string BindPassword { get; set; } = "";
    public bool BindPasswordStored { get; set; }
    public string SearchBase { get; set; } = "";
    public string UserFilter { get; set; } = "(uid={0})";
    public string LdapEmailAttribute { get; set; } = "mail";
    public string LdapNationalIdAttribute { get; set; } = "";

    public static SsoFormModel From(SsoProviderDto p)
    {
        var m = new SsoFormModel { Kind = p.Kind, Slug = p.Slug, NameAr = p.DisplayNameAr, NameEn = p.DisplayNameEn, Enabled = p.Enabled };
        if (p.Saml is { } s)
        {
            (m.MetadataUrl, m.EntityId, m.SsoUrl, m.CertFingerprint, m.CertExpires, m.NameIdFormat, m.SamlEmailAttribute, m.SamlNationalIdAttribute) =
                (s.MetadataUrl, s.EntityId, s.SsoUrl, s.CertFingerprint, s.CertExpires, s.NameIdFormat, s.EmailAttribute, s.NationalIdAttribute);
        }
        if (p.Ldap is { } l)
        {
            (m.Host, m.Port, m.Transport, m.BindDn, m.BindPasswordStored, m.SearchBase, m.UserFilter, m.LdapEmailAttribute, m.LdapNationalIdAttribute) =
                (l.Host, l.Port, l.Transport, l.BindDn, l.BindPasswordStored, l.SearchBase, l.UserFilter, l.EmailAttribute, l.NationalIdAttribute);
        }
        return m;
    }

    public SaveSsoProviderRequest ToRequest() => new(Kind, Slug.Trim(), NameAr.Trim(), NameEn.Trim(), Enabled,
        Kind == SsoKinds.Saml ? new SamlSettingsDto(UseUpload ? "" : MetadataUrl.Trim(), EntityId.Trim(), SsoUrl.Trim(), CertFingerprint, CertExpires, NameIdFormat, SamlEmailAttribute.Trim(), SamlNationalIdAttribute.Trim()) : null,
        Kind == SsoKinds.Saml && UseUpload ? MetadataXml : null,
        Kind == SsoKinds.Ldap ? new LdapSettingsDto(Host.Trim(), Port, Transport, BindDn.Trim(), BindPasswordStored, SearchBase.Trim(), UserFilter.Trim(), LdapEmailAttribute.Trim(), LdapNationalIdAttribute.Trim()) : null,
        Kind == SsoKinds.Ldap && !string.IsNullOrEmpty(BindPassword) ? BindPassword : null);
}
