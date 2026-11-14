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

    public static string Redact(string value) => value.Contains("/p/", StringComparison.Ordinal) ? TokenSegment().Replace(value, Redacted) : value;
}

/// <summary>Serilog enricher that redacts the token segment from every string property of every log event (request logs, hosting diagnostics).</summary>
public sealed class TokenScrubbingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var property in logEvent.Properties.ToList())
        {
            if (property.Value is ScalarValue { Value: string text } && text.Contains("/p/", StringComparison.Ordinal))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, new ScalarValue(LogScrubbing.Redact(text))));
            }
        }
    }
}
