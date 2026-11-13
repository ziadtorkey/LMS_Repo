using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace Lms.Api.PackageHost;

/// <summary>Keeps package tokens out of logs: <c>/p/{token}/...</c> becomes <c>/p/[redacted]/...</c> (ADR 0005 section 5, X-05).</summary>
public static partial class LogScrubbing
{
    public const string Redacted = "[redacted]";

    [GeneratedRegex("(?<=/p/)[^/\\s?#\"]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenSegment();

    // A Data Protection payload always starts "CfDJ": a token is redacted wherever it appears (route values of Debug logs have no leading "/p/").
    [GeneratedRegex("CfDJ[A-Za-z0-9_%-]{20,}", RegexOptions.CultureInvariant)]
    private static partial Regex ProtectedPayload();

    public static bool MayContainToken(string value) => value.Contains("/p/", StringComparison.Ordinal) || value.Contains("CfDJ", StringComparison.Ordinal);

    public static string Redact(string value) =>
        MayContainToken(value) ? ProtectedPayload().Replace(TokenSegment().Replace(value, Redacted), Redacted) : value;
}

/// <summary>Serilog enricher that redacts the token segment from every string property of every log event (request logs, hosting diagnostics).</summary>
public sealed class TokenScrubbingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var property in logEvent.Properties.ToList())
        {
            if (property.Value is ScalarValue { Value: string text } && LogScrubbing.MayContainToken(text))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, new ScalarValue(LogScrubbing.Redact(text))));
            }
        }
    }
}
