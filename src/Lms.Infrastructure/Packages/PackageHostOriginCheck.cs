using System.Net;

namespace Lms.Infrastructure.Packages;

/// <summary>Bound from the <c>PackageHost</c> configuration section.</summary>
public sealed class PackageHostOptions
{
    public const string Section = "PackageHost";

    /// <summary>The origin of the content host, for example <c>https://lms-content.example</c>; empty = not configured (every package route fails closed).</summary>
    public string? Origin { get; set; }

    /// <summary>The origin of the app (<c>frame-ancestors</c> of the content host); defaults to <c>App:PublicBaseUrl</c>.</summary>
    public string? AppOrigin { get; set; }

    /// <summary>How long a revocation lookup is cached per instance, in seconds (ADR 0005: the revocation bound is 30 s per node).</summary>
    public int RevocationCacheSeconds { get; set; } = 30;

    /// <summary>True when the content host is a separate registrable domain from the app (D56); every package route and the host pipeline fail closed otherwise.</summary>
    public bool IsConfigured => PackageHostOriginCheck.IsSeparate(Origin, AppOrigin ?? string.Empty);
}

/// <summary>
/// D56 / Decision 10: package content is served from a separate registrable domain, never a subdomain of the app's, so cookies and
/// <c>SameSite</c> never treat it as the same site. <see cref="IsSeparate"/> is the check every package route runs; when it fails the
/// route answers 409 <c>package_host_not_configured</c> (there is no fallback to the app origin).
/// </summary>
public static class PackageHostOriginCheck
{
    // Public suffixes of two labels that the sites of this product use; the registrable domain then has three labels.
    private static readonly HashSet<string> TwoLabelSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "org.uk", "ac.uk", "gov.uk", "com.au", "net.au", "org.au", "edu.au", "co.nz", "co.za", "com.sa", "net.sa", "org.sa", "edu.sa",
        "gov.sa", "med.sa", "com.eg", "edu.eg", "gov.eg", "org.eg", "com.ae", "co.ae", "ac.ae", "gov.ae", "com.kw", "edu.kw", "com.qa", "edu.qa",
        "com.bh", "edu.bh", "com.om", "edu.om", "com.jo", "edu.jo", "com.tr", "com.br", "co.in", "co.jp", "com.cn", "com.sg",
    };

    /// <summary>True when <paramref name="contentOrigin"/> is a well-formed http(s) origin on another registrable domain than <paramref name="appOrigin"/>.</summary>
    public static bool IsSeparate(string? contentOrigin, string appOrigin)
    {
        if (!TryHost(contentOrigin, out var content) || !TryHost(appOrigin, out var app))
        {
            return false;
        }

        return !string.Equals(RegistrableDomain(content), RegistrableDomain(app), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The site of a host: an IP address and a single-label host are their own site, <c>localhost</c> names share one, otherwise the last two labels (three under a known two-label suffix).</summary>
    public static string RegistrableDomain(string host)
    {
        host = host.TrimEnd('.').ToLowerInvariant();
        if (IPAddress.TryParse(host.Trim('[', ']'), out _))
        {
            return host;
        }

        var labels = host.Split('.');
        if (labels.Length <= 1)
        {
            return host;
        }

        if (labels[^1] == "localhost")
        {
            return "localhost";
        }

        var take = labels.Length >= 3 && TwoLabelSuffixes.Contains(string.Join('.', labels[^2..])) ? 3 : 2;
        return string.Join('.', labels[^Math.Min(take, labels.Length)..]);
    }

    private static bool TryHost(string? origin, out string host)
    {
        host = string.Empty;
        if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        host = uri.Host;
        return true;
    }
}
